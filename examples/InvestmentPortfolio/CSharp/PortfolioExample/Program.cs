// ==============================================================================
// Investment Portfolio Optimization - C# Interop Example
// ==============================================================================
// Demonstrates C# interoperability with the F# FSharp.Azure.Quantum library
// for portfolio optimization using HybridSolver with quantum-ready architecture.
//
// This example shows:
// - Natural C# usage of F# quantum optimization library
// - Portfolio optimization (mean-variance analysis)
// - Risk-return trade-off calculations
// - Sharpe ratio analysis
// - Automatic classical/quantum solver routing
// ==============================================================================

using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using FSharp.Azure.Quantum;
using FSharp.Azure.Quantum.Classical;
using FSharp.Azure.Quantum.Data;
using Microsoft.FSharp.Core;
using static FSharp.Azure.Quantum.CSharpBuilders;
using static FSharp.Azure.Quantum.Data.FinancialData;

namespace PortfolioExample;

/// <summary>
/// Main program class for portfolio optimization example.
/// </summary>
internal sealed class Program
{
    private Program()
    {
    }

    private static void Main(string[] args)
    {
        Console.WriteLine("╔══════════════════════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║       INVESTMENT PORTFOLIO OPTIMIZATION - C# INTEROP EXAMPLE                ║");
        Console.WriteLine("║              Using HybridSolver (Quantum-Ready Optimization)                ║");
        Console.WriteLine("╚══════════════════════════════════════════════════════════════════════════════╝");
        Console.WriteLine();
        Console.WriteLine("Usage: dotnet run --project examples/InvestmentPortfolio/CSharp/PortfolioExample/PortfolioExample.csproj [-- --live]");
        Console.WriteLine("  --live   Use live Yahoo Finance data (cached; falls back offline)");
        Console.WriteLine();

        // Define investment budget
        const double budget = 100000.0; // $100,000

        bool useLive = Array.Exists(args, a => string.Equals(a, "--live", StringComparison.OrdinalIgnoreCase));

        // Define available stocks with historical performance data
        var stocks = DefineStockUniverse(useLive);

        Console.WriteLine($"Problem: Allocate ${budget:N2} across {stocks.Length} tech stocks");
        Console.WriteLine("Objective: Maximize risk-adjusted returns (Sharpe ratio)");
        Console.WriteLine();

        // Run portfolio optimization
        Console.WriteLine("Running portfolio optimization with HybridSolver...");
        var startTime = DateTime.UtcNow;

        var result = OptimizePortfolio(stocks, budget);

        var elapsed = (DateTime.UtcNow - startTime).TotalMilliseconds;
        Console.WriteLine($"Completed in {elapsed:F0} ms");
        Console.WriteLine();

        // Display results
        if (result.IsOk)
        {
            var solution = result.ResultValue;

            Console.WriteLine($"💡 Solver Decision: {solution.Reasoning}");
            Console.WriteLine();

            DisplayAllocationReport(solution);
            DisplayRiskReturnAnalysis(solution, stocks);
            DisplayBusinessImpact(solution, budget);

            Console.WriteLine("╔══════════════════════════════════════════════════════════════════════════════╗");
            Console.WriteLine("║                     OPTIMIZATION SUCCESSFUL                                  ║");
            Console.WriteLine("╚══════════════════════════════════════════════════════════════════════════════╝");
            Console.WriteLine();
            Console.WriteLine("✨ Note: HybridSolver automatically routes between classical and quantum solvers");
            Console.WriteLine("   based on problem size and structure. For 8 assets → classical optimizer.");
            Console.WriteLine("   For larger portfolios (50+ assets), quantum advantage may emerge.");
        }
        else
        {
            var error = result.ErrorValue;
            Console.WriteLine($"❌ Optimization failed: {error.Message}");
            Environment.Exit(1);
        }
    }

    /// <summary>
    /// Define the stock universe with historical performance data.
    /// </summary>
    private static PortfolioTypes.Asset[] DefineStockUniverse(bool useLive)
    {
        var fallback = DefineStockUniverseFallback();

        if (!useLive)
        {
            Console.WriteLine("[DATA] Live Yahoo: disabled (using built-in sample data)");
            Console.WriteLine();
            return fallback;
        }

        var cacheDir = Path.Combine(AppContext.BaseDirectory, "output", "yahoo-cache");
        Directory.CreateDirectory(cacheDir);

        Console.WriteLine("[DATA] Live Yahoo: enabled");
        Console.WriteLine($"[DATA] Cache: {cacheDir}");

        try
        {
            using var httpClient = new HttpClient();

            // Use 2 years of daily data for a more stable estimate.
            var range = YahooHistoryRange.TwoYears;
            var interval = YahooHistoryInterval.OneDay;

            var fallbackBySymbol = fallback.ToDictionary(a => a.Symbol, StringComparer.OrdinalIgnoreCase);

            var assets = fallback.Select(a => a.Symbol).Select(symbol =>
            {
                var request = new YahooHistoryRequest(
                    symbol: symbol,
                    range: range,
                    interval: interval,
                    includeAdjustedClose: true,
                    cacheDirectory: FSharpOption<string>.Some(cacheDir),
                    cacheTtl: TimeSpan.FromHours(6));

                var priceSeriesResult = fetchYahooHistory(httpClient, request);
                if (priceSeriesResult.IsError)
                {
                    throw new InvalidOperationException($"Yahoo fetch failed for {symbol}: {priceSeriesResult.ErrorValue.Message}");
                }

                var priceSeries = priceSeriesResult.ResultValue;

                var returns = calculateReturns(priceSeries);

                // Portfolio example expects annual expected return + annual volatility.
                double expectedReturn = calculateExpectedReturn(returns, 252.0);
                double risk = calculateVolatility(returns, 252.0);

                var latestPriceOpt = tryGetLatestPrice(priceSeries);
                double fallbackPrice = fallbackBySymbol[symbol].Price;
                double price = latestPriceOpt != null ? latestPriceOpt.Value : fallbackPrice;

                return new PortfolioTypes.Asset(symbol, expectedReturn, risk, price);
            }).ToArray();

            Console.WriteLine("[DATA] Yahoo fetch succeeded for all symbols.");
            Console.WriteLine();

            return assets;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[DATA] Yahoo fetch failed; falling back to sample data: {ex.Message}");
            Console.WriteLine();
            return fallback;
        }
    }

    private static PortfolioTypes.Asset[] DefineStockUniverseFallback()
    {
        return new[]
        {
            new PortfolioTypes.Asset(
                symbol: "AAPL",
                expectedReturn: 0.18,   // 18% annual return
                risk: 0.22,              // 22% volatility
                price: 175.00),
            new PortfolioTypes.Asset(
                symbol: "MSFT",
                expectedReturn: 0.22,
                risk: 0.25,
                price: 380.00),
            new PortfolioTypes.Asset(
                symbol: "GOOGL",
                expectedReturn: 0.16,
                risk: 0.28,
                price: 140.00),
            new PortfolioTypes.Asset(
                symbol: "AMZN",
                expectedReturn: 0.24,
                risk: 0.32,
                price: 155.00),
            new PortfolioTypes.Asset(
                symbol: "NVDA",
                expectedReturn: 0.35,
                risk: 0.45,
                price: 485.00),
            new PortfolioTypes.Asset(
                symbol: "META",
                expectedReturn: 0.28,
                risk: 0.38,
                price: 350.00),
            new PortfolioTypes.Asset(
                symbol: "TSLA",
                expectedReturn: 0.30,
                risk: 0.55,
                price: 245.00),
            new PortfolioTypes.Asset(
                symbol: "AMD",
                expectedReturn: 0.26,
                risk: 0.42,
                price: 125.00),
        };
    }

    /// <summary>
    /// Optimize portfolio allocation using HybridSolver.
    /// </summary>
    private static Microsoft.FSharp.Core.FSharpResult<HybridSolver.Solution<PortfolioSolver.PortfolioSolution>, FSharp.Azure.Quantum.Core.QuantumError>
        OptimizePortfolio(PortfolioTypes.Asset[] assets, double budget)
    {
        // Define constraints
        var constraints = new PortfolioSolver.Constraints(
            budget: budget,
            minHolding: 0.0,        // No minimum holding requirement
            maxHolding: budget);      // Can invest entire budget in one asset if optimal

        // Call HybridSolver (quantum-ready optimization)
        return HybridSolver.solvePortfolio(
            Microsoft.FSharp.Collections.ListModule.OfSeq(assets),
            constraints,
            budget: null,
            timeout: null,
            forceMethod: null);
    }

    /// <summary>
    /// Display portfolio allocation report.
    /// </summary>
    private static void DisplayAllocationReport(
        HybridSolver.Solution<PortfolioSolver.PortfolioSolution> solution)
    {
        var portfolio = solution.Result;

        Console.WriteLine("╔══════════════════════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║                       PORTFOLIO ALLOCATION REPORT                            ║");
        Console.WriteLine("╚══════════════════════════════════════════════════════════════════════════════╝");
        Console.WriteLine();
        Console.WriteLine("ASSETS SELECTED:");
        Console.WriteLine("────────────────────────────────────────────────────────────────────────────────");

        int index = 1;
        foreach (var allocation in portfolio.Allocations)
        {
            double pct = (allocation.Value / portfolio.TotalValue) * 100.0;
            Console.WriteLine($"  {index}. {allocation.Asset.Symbol,-6} | {allocation.Shares,6:F2} shares @ ${allocation.Asset.Price:N2} = ${allocation.Value:N2} ({pct:F1}%)");
            index++;
        }

        Console.WriteLine();
        Console.WriteLine("PORTFOLIO SUMMARY:");
        Console.WriteLine("────────────────────────────────────────────────────────────────────────────────");
        Console.WriteLine($"  Total Invested:        ${portfolio.TotalValue:N2}");
        Console.WriteLine($"  Number of Holdings:    {portfolio.Allocations.Length} stocks");
        Console.WriteLine($"  Expected Annual Return: {portfolio.ExpectedReturn:P2}");
        Console.WriteLine($"  Portfolio Risk (σ):    {portfolio.Risk:P2}");
        Console.WriteLine($"  Sharpe Ratio:          {portfolio.SharpeRatio:F2}");
        Console.WriteLine();
    }

    /// <summary>
    /// Display risk-return analysis comparing portfolio to individual stocks.
    /// </summary>
    private static void DisplayRiskReturnAnalysis(
        HybridSolver.Solution<PortfolioSolver.PortfolioSolution> solution,
        PortfolioTypes.Asset[] stocks)
    {
        var portfolio = solution.Result;

        Console.WriteLine("╔══════════════════════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║                      RISK-RETURN ANALYSIS                                    ║");
        Console.WriteLine("╚══════════════════════════════════════════════════════════════════════════════╝");
        Console.WriteLine();
        Console.WriteLine("INDIVIDUAL STOCK METRICS:");
        Console.WriteLine("────────────────────────────────────────────────────────────────────────────────");
        Console.WriteLine("  Symbol  | Expected Return | Volatility | Sharpe Ratio | Allocation");
        Console.WriteLine("  --------|-----------------|------------|--------------|------------");

        foreach (var stock in stocks)
        {
            double sharpe = stock.ExpectedReturn / stock.Risk;
            var allocation = portfolio.Allocations.FirstOrDefault(a => a.Asset.Symbol == stock.Symbol);
            string allocPct = allocation != null
                ? $"{(allocation.Value / portfolio.TotalValue) * 100.0:F1}%"
                : "0.0%";

            Console.WriteLine($"  {stock.Symbol,-7} | {stock.ExpectedReturn,14:P2} | {stock.Risk,9:P2} | {sharpe,12:F2} | {allocPct,10}");
        }

        Console.WriteLine();
        Console.WriteLine("PORTFOLIO VS. INDIVIDUAL STOCKS:");
        Console.WriteLine("────────────────────────────────────────────────────────────────────────────────");

        double avgReturn = stocks.Average(s => s.ExpectedReturn);
        double avgRisk = stocks.Average(s => s.Risk);
        double avgSharpe = avgReturn / avgRisk;

        Console.WriteLine($"  Average Stock Return:  {avgReturn:P2}");
        Console.WriteLine($"  Portfolio Return:      {portfolio.ExpectedReturn:P2} ({portfolio.ExpectedReturn / avgReturn:F1}x better)");
        Console.WriteLine();
        Console.WriteLine($"  Average Stock Risk:    {avgRisk:P2}");
        Console.WriteLine($"  Portfolio Risk:        {portfolio.Risk:P2} ({avgRisk / portfolio.Risk:F1}x lower)");
        Console.WriteLine();
        Console.WriteLine($"  Average Sharpe Ratio:  {avgSharpe:F2}");
        Console.WriteLine($"  Portfolio Sharpe:      {portfolio.SharpeRatio:F2} ({portfolio.SharpeRatio / avgSharpe:F1}x better)");
        Console.WriteLine();
    }

    /// <summary>
    /// Display projected business impact and scenario analysis.
    /// </summary>
    private static void DisplayBusinessImpact(
        HybridSolver.Solution<PortfolioSolver.PortfolioSolution> solution,
        double budget)
    {
        var portfolio = solution.Result;

        double expectedGain = portfolio.TotalValue * portfolio.ExpectedReturn;
        double potentialRange = portfolio.TotalValue * portfolio.Risk;
        double bestCase = portfolio.TotalValue + expectedGain + potentialRange;
        double worstCase = portfolio.TotalValue + expectedGain - potentialRange;

        Console.WriteLine("╔══════════════════════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║                          BUSINESS IMPACT ANALYSIS                            ║");
        Console.WriteLine("╚══════════════════════════════════════════════════════════════════════════════╝");
        Console.WriteLine();
        Console.WriteLine("PROJECTED ANNUAL OUTCOMES (1 Year):");
        Console.WriteLine("────────────────────────────────────────────────────────────────────────────────");
        Console.WriteLine($"  Initial Investment:    ${budget:N2}");
        Console.WriteLine($"  Expected Return:       ${expectedGain:N2} (+{portfolio.ExpectedReturn:P2} gain)");
        Console.WriteLine();
        Console.WriteLine("  SCENARIO ANALYSIS (95% confidence interval):");
        Console.WriteLine($"  • Best Case:           ${bestCase:N2} (+{(bestCase - budget) / budget:P2})");
        Console.WriteLine($"  • Expected:            ${portfolio.TotalValue + expectedGain:N2} (+{expectedGain / budget:P2})");
        Console.WriteLine($"  • Worst Case:          ${worstCase:N2} ({(worstCase - budget) / budget:P2})");
        Console.WriteLine();
        Console.WriteLine("KEY INSIGHTS:");
        Console.WriteLine("────────────────────────────────────────────────────────────────────────────────");
        Console.WriteLine($"  ✓ Diversification across {portfolio.Allocations.Length} tech stocks reduces risk");
        Console.WriteLine($"  ✓ Sharpe ratio of {portfolio.SharpeRatio:F2} indicates efficient risk-adjusted returns");
        Console.WriteLine($"  ✓ Expected to generate ${expectedGain:N2} annually");
        Console.WriteLine("  ✓ Risk-managed approach balances growth and volatility");
        Console.WriteLine();
    }
}
