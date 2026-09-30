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
//
// Figures: annual return, volatility and correlations estimated from Yahoo
// Finance daily adjusted closes 2019-2023 (examples/InvestmentPortfolio/data/
// market-stats-2019-2023.json, copied next to the program); the buy price is
// the 2023-12-29 adjusted close and 2024 is the hold-out year. --live
// recomputes the same figures with FinancialData.fetchYahooHistoryAsync.
// ==============================================================================

using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FSharp.Azure.Quantum;
using FSharp.Azure.Quantum.Classical;
using FSharp.Azure.Quantum.Data;
using Microsoft.FSharp.Core;
using static FSharp.Azure.Quantum.Data.FinancialData;

namespace PortfolioExample;

/// <summary>Per-symbol figures over the estimation window (see data/README.md).</summary>
internal sealed record AssetFigures(
    string Symbol,
    string Name,
    double AnnualReturn,
    double AnnualVolatility,
    int Observations,
    string BuyDate,
    double BuyPrice,
    double? HoldoutReturn);

/// <summary>Pearson correlations of daily returns.</summary>
internal sealed record CorrelationFigures(string[] Symbols, double[][] Matrix, int Observations);

/// <summary>Figures for a set of symbols over one estimation window and its hold-out year.</summary>
internal sealed record MarketStats(
    string WindowFrom,
    string WindowTo,
    string HoldoutFrom,
    string HoldoutTo,
    string GeneratedOn,
    AssetFigures[] Assets,
    CorrelationFigures Correlation);

/// <summary>
/// Main program class for portfolio optimization example.
/// </summary>
internal sealed class Program
{
    private const string BundledFile = "market-stats-2019-2023.json";

    private static readonly string[] PresetSymbols = ["AAPL", "MSFT", "GOOGL", "AMZN", "NVDA", "META", "TSLA", "AMD"];

    private Program()
    {
    }

    private static async Task Main(string[] args)
    {
        Console.WriteLine("╔══════════════════════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║       INVESTMENT PORTFOLIO OPTIMIZATION - C# INTEROP EXAMPLE                ║");
        Console.WriteLine("║              Using HybridSolver (Quantum-Ready Optimization)                ║");
        Console.WriteLine("╚══════════════════════════════════════════════════════════════════════════════╝");
        Console.WriteLine();
        Console.WriteLine("Usage: dotnet run --project examples/InvestmentPortfolio/CSharp/PortfolioExample/PortfolioExample.csproj [-- --live [--symbols A,B,C] [--from yyyy-MM-dd] [--to yyyy-MM-dd]]");
        Console.WriteLine("  (default)  Bundled figures: Yahoo Finance adjusted closes 2019-2023, hold-out 2024");
        Console.WriteLine("  --live     Recompute the figures from Yahoo Finance (cached 6 h; falls back to the bundled figures)");
        Console.WriteLine();

        // Define investment budget
        const double budget = 100000.0; // $100,000

        bool useLive = Array.Exists(args, a => string.Equals(a, "--live", StringComparison.OrdinalIgnoreCase));
        if (!useLive && (OptionValue(args, "--from") is not null || OptionValue(args, "--to") is not null))
        {
            Console.WriteLine("❌ --from/--to need --live; the bundled figures cover 2019-01-01..2023-12-31.");
            Environment.Exit(1);
        }

        string[] symbols = OptionValue(args, "--symbols") is { } list
            ? list.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(s => s.ToUpperInvariant()).ToArray()
            : PresetSymbols;
        DateTime from = ParseDateOption(args, "--from", new DateTime(2019, 1, 1));
        DateTime to = ParseDateOption(args, "--to", new DateTime(2023, 12, 31));

        var market = useLive
            ? await LoadLiveOrBundledAsync(symbols, from, to).ConfigureAwait(false)
            : LoadBundled();

        var stocks = DefineStockUniverse(market, symbols);
        if (stocks.Length == 0)
        {
            Console.WriteLine("❌ No symbols with figures; the bundled data covers " + string.Join(",", market.Assets.Select(a => a.Symbol)) + ". Use --live for others.");
            Environment.Exit(1);
        }

        Console.WriteLine($"[DATA] Yahoo Finance adjusted closes, daily returns {market.WindowFrom}..{market.WindowTo} ({(useLive ? "computed at run time" : "bundled, generated " + market.GeneratedOn)})");
        Console.WriteLine();
        Console.WriteLine($"Problem: Allocate ${budget:N2} across {stocks.Length} stocks");
        Console.WriteLine("Objective: Maximize risk-adjusted returns (Sharpe ratio)");
        Console.WriteLine();

        // Run portfolio optimization
        Console.WriteLine("Running portfolio optimization with HybridSolver...");
        var startTime = DateTime.UtcNow;

        // Covariance S_ij = rho_ij * sigma_i * sigma_j from the window correlations.
        var covariance = Covariance(market, stocks);
        var result = OptimizePortfolio(stocks, budget, covariance);

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
            DisplayOneYearRange(solution, market, budget);
            DisplayHoldout(solution, market, stocks);

            Console.WriteLine("╔══════════════════════════════════════════════════════════════════════════════╗");
            Console.WriteLine("║                     OPTIMIZATION SUCCESSFUL                                  ║");
            Console.WriteLine("╚══════════════════════════════════════════════════════════════════════════════╝");
            Console.WriteLine();
            Console.WriteLine($"Note: HybridSolver chose the {solution.Method} solver for {stocks.Length} assets and received the");
            Console.WriteLine($"   {market.WindowFrom}..{market.WindowTo} covariance, so the risk above is sqrt(w' S w). The classical path");
            Console.WriteLine("   picks assets by their own return/risk ratio; the quantum path (QAOA) minimises the");
            Console.WriteLine("   mean-variance QUBO, covariance terms included.");
        }
        else
        {
            var error = result.ErrorValue;
            Console.WriteLine($"❌ Optimization failed: {error.Message}");
            Environment.Exit(1);
        }
    }

    /// <summary>
    /// Define the stock universe from the market figures (annual return, volatility, buy price).
    /// </summary>
    private static PortfolioTypes.Asset[] DefineStockUniverse(MarketStats market, string[] symbols)
    {
        return symbols
            .Select(symbol => TryAsset(market, symbol))
            .OfType<AssetFigures>()
            .Select(a => new PortfolioTypes.Asset(a.Symbol, a.AnnualReturn, a.AnnualVolatility, a.BuyPrice))
            .ToArray();
    }

    // --------------------------------------------------------------------------
    // Market figures: bundled file or Yahoo Finance
    // --------------------------------------------------------------------------

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private static MarketStats LoadBundled()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "data", BundledFile);
        return JsonSerializer.Deserialize<MarketStats>(File.ReadAllText(path), JsonOptions)
            ?? throw new InvalidDataException($"Empty market figures file: {path}");
    }

    private static async Task<MarketStats> LoadLiveOrBundledAsync(string[] symbols, DateTime from, DateTime to)
    {
        var cacheDir = Path.Combine(Path.GetTempPath(), "FSharp.Azure.Quantum", "yahoo-cache");
        Directory.CreateDirectory(cacheDir);
        Console.WriteLine($"[DATA] Fetching {symbols.Length} symbols from Yahoo Finance (cache: {cacheDir}, 6 h)");

        using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };

        var series = new List<(string Symbol, (DateTime Date, double Price)[] Bars)>();

        foreach (var symbol in symbols)
        {
            // Explicit bounds: 10 days before the window (the close the first return starts from)
            // to the end of the hold-out year.
            var request = new YahooHistoryRequest(
                symbol: symbol,
                range: YahooHistoryRange.Max,
                interval: YahooHistoryInterval.OneDay,
                includeAdjustedClose: true,
                cacheDirectory: FSharpOption<string>.Some(cacheDir),
                cacheTtl: TimeSpan.FromHours(6),
                startDate: FSharpOption<DateTime>.Some(from.AddDays(-10)),
                endDate: FSharpOption<DateTime>.Some(to.AddYears(1)));

            var result = await fetchYahooHistoryAsync(httpClient, request, CancellationToken.None).ConfigureAwait(false);
            if (result.IsError)
            {
                Console.WriteLine($"[DATA] Dropped {symbol}: {result.ErrorValue.Message}");
                continue;
            }

            var bars = result.ResultValue.Prices
                .Where(b => b.AdjustedClose != null && b.AdjustedClose.Value > 0.0)
                .Select(b => (b.Date, Price: b.AdjustedClose.Value))
                .DistinctBy(b => b.Date)
                .OrderBy(b => b.Date)
                .ToArray();

            bool covers = bars.Length > 1
                && bars[0].Date <= from.AddDays(7)
                && bars.Any(b => b.Date >= to.AddDays(-7) && b.Date <= to);
            if (!covers)
            {
                Console.WriteLine($"[DATA] Dropped {symbol}: no adjusted closes for the whole window");
                continue;
            }

            series.Add((symbol, bars));
        }

        if (series.Count == 0)
        {
            Console.WriteLine("[DATA] Yahoo fetch gave no usable data; using the bundled figures.");
            return LoadBundled();
        }

        return ComputeStats(series, from, to);
    }

    /// <summary>
    /// Daily simple returns of adjusted closes dated inside the window: annual return = mean * 252,
    /// annual volatility = sample std * sqrt 252, Pearson correlation on shared dates, buy price = last
    /// adjusted close of the window, hold-out = buy-and-hold return over the following year.
    /// </summary>
    private static MarketStats ComputeStats(List<(string Symbol, (DateTime Date, double Price)[] Bars)> series, DateTime from, DateTime to)
    {
        var holdoutTo = to.AddYears(1);

        var returns = series.ToDictionary(
            s => s.Symbol,
            s => s.Bars.Zip(s.Bars.Skip(1), (p0, p1) => (p1.Date, Return: p1.Price / p0.Price - 1.0))
                .Where(r => r.Date >= from && r.Date <= to)
                .ToArray());

        var assets = series.Select(s =>
        {
            var r = returns[s.Symbol].Select(x => x.Return).ToArray();
            var buy = s.Bars.Last(b => b.Date <= to);
            var hold = s.Bars.LastOrDefault(b => b.Date > to && b.Date <= holdoutTo);
            double? holdout = hold.Date >= holdoutTo.AddDays(-7) ? hold.Price / buy.Price - 1.0 : null;
            return new AssetFigures(
                s.Symbol,
                s.Symbol,
                r.Average() * 252.0,
                SampleStd(r) * Math.Sqrt(252.0),
                r.Length,
                buy.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                buy.Price,
                holdout);
        }).ToArray();

        var common = returns.Values
            .Select(v => v.Select(x => x.Date))
            .Aggregate((a, b) => a.Intersect(b))
            .OrderBy(d => d)
            .ToArray();
        var columns = series
            .Select(s =>
            {
                var byDate = returns[s.Symbol].ToDictionary(x => x.Date, x => x.Return);
                return common.Select(d => byDate[d]).ToArray();
            })
            .ToArray();
        var matrix = columns.Select((ci, i) => columns.Select((cj, j) => i == j ? 1.0 : Pearson(ci, cj)).ToArray()).ToArray();

        return new MarketStats(
            from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            to.AddDays(1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            holdoutTo.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            assets,
            new CorrelationFigures(series.Select(s => s.Symbol).ToArray(), matrix, common.Length));
    }

    private static double SampleStd(double[] xs)
    {
        double m = xs.Average();
        return Math.Sqrt(xs.Sum(x => (x - m) * (x - m)) / (xs.Length - 1));
    }

    private static double Pearson(double[] a, double[] b)
    {
        double ma = a.Average();
        double mb = b.Average();
        double cov = a.Zip(b, (x, y) => (x - ma) * (y - mb)).Sum();
        double va = a.Sum(x => (x - ma) * (x - ma));
        double vb = b.Sum(y => (y - mb) * (y - mb));
        return va == 0.0 || vb == 0.0 ? 0.0 : cov / Math.Sqrt(va * vb);
    }

    private static AssetFigures? TryAsset(MarketStats market, string symbol) =>
        market.Assets.FirstOrDefault(a => string.Equals(a.Symbol, symbol, StringComparison.OrdinalIgnoreCase));

    private static string? OptionValue(string[] args, string name)
    {
        int i = Array.FindIndex(args, a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase));
        return i >= 0 && i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal) ? args[i + 1] : null;
    }

    private static DateTime ParseDateOption(string[] args, string name, DateTime fallback)
    {
        var value = OptionValue(args, name);
        if (value is null)
        {
            return fallback;
        }

        if (DateTime.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            return date;
        }

        Console.WriteLine($"❌ {name} must be yyyy-MM-dd, got '{value}'");
        Environment.Exit(1);
        return fallback;
    }

    // --------------------------------------------------------------------------
    // Portfolio evaluation against the figures
    // --------------------------------------------------------------------------

    private static (string Symbol, double Weight)[] Normalise(IEnumerable<(string Symbol, double Value)> values)
    {
        var list = values.Where(v => v.Value > 0.0).ToArray();
        double total = list.Sum(v => v.Value);
        return total > 0.0 ? list.Select(v => (v.Symbol, v.Value / total)).ToArray() : [];
    }

    private static double? ExpectedReturn(MarketStats market, (string Symbol, double Weight)[] weights)
    {
        var terms = weights.Select(w => TryAsset(market, w.Symbol) is { } a ? w.Weight * a.AnnualReturn : (double?)null).ToArray();
        return terms.All(t => t.HasValue) ? terms.Sum() : null;
    }

    /// <summary>
    /// Covariance S_ij = rho_ij * sigma_i * sigma_j of the stocks, in their order, from the window
    /// correlations and each stock's volatility (PortfolioTypes.covarianceFromCorrelation).
    /// </summary>
    private static double[,] Covariance(MarketStats market, PortfolioTypes.Asset[] stocks)
    {
        var index = stocks
            .Select(s => Array.FindIndex(market.Correlation.Symbols, c => string.Equals(c, s.Symbol, StringComparison.OrdinalIgnoreCase)))
            .ToArray();
        if (index.Any(i => i < 0))
        {
            throw new InvalidDataException("A selected symbol has no correlation row.");
        }

        var rho = new double[stocks.Length, stocks.Length];
        for (int i = 0; i < stocks.Length; i++)
        {
            for (int j = 0; j < stocks.Length; j++)
            {
                rho[i, j] = market.Correlation.Matrix[index[i]][index[j]];
            }
        }

        var covariance = PortfolioTypes.covarianceFromCorrelation(stocks.Select(s => s.Risk).ToArray(), rho);
        return covariance.IsOk ? covariance.ResultValue : throw new InvalidDataException(covariance.ErrorValue.Message);
    }

    /// <summary>Annual volatility sqrt(w' S w) of weights over the window figures.</summary>
    private static double? VolatilityWithCorrelations(MarketStats market, (string Symbol, double Weight)[] weights)
    {
        var assets = weights.Select(w => TryAsset(market, w.Symbol)).ToArray();
        if (assets.Any(a => a is null)
            || weights.Any(w => !market.Correlation.Symbols.Contains(w.Symbol, StringComparer.OrdinalIgnoreCase)))
        {
            return null;
        }

        var stocks = assets.Select(a => new PortfolioTypes.Asset(a!.Symbol, a.AnnualReturn, a.AnnualVolatility, a.BuyPrice)).ToArray();
        return Math.Sqrt(Math.Max(0.0, PortfolioTypes.portfolioVariance(weights.Select(w => w.Weight).ToArray(), Covariance(market, stocks))));
    }

    private static double? HoldoutReturn(MarketStats market, (string Symbol, double Weight)[] weights)
    {
        var terms = weights.Select(w => TryAsset(market, w.Symbol)?.HoldoutReturn is { } r ? w.Weight * r : (double?)null).ToArray();
        return terms.All(t => t.HasValue) ? terms.Sum() : null;
    }

    private static string Pct(double? x, bool signed = false) =>
        x is { } v ? (signed ? v.ToString("+0.0%;-0.0%", CultureInfo.InvariantCulture) : v.ToString("0.0%", CultureInfo.InvariantCulture)) : "n/a";

    /// <summary>
    /// Optimize portfolio allocation using HybridSolver with the covariance of the asset returns.
    /// </summary>
    private static Microsoft.FSharp.Core.FSharpResult<HybridSolver.Solution<PortfolioSolver.PortfolioSolution>, FSharp.Azure.Quantum.Core.QuantumError>
        OptimizePortfolio(PortfolioTypes.Asset[] assets, double budget, double[,] covariance)
    {
        // Define constraints
        var constraints = new PortfolioSolver.Constraints(
            budget: budget,
            minHolding: 0.0,        // No minimum holding requirement
            maxHolding: budget);      // Can invest entire budget in one asset if optimal

        // Call HybridSolver (quantum-ready optimization); risk is sqrt(w' S w)
        return HybridSolver.solvePortfolioWithCovariance(
            Microsoft.FSharp.Collections.ListModule.OfSeq(assets),
            covariance,
            constraints,
            budget: null,
            timeout: null,
            forceMethod: null,
            backend: null);
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
        Console.WriteLine("  Symbol  | Expected Return | Volatility |  Return/Risk | Allocation");
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
        Console.WriteLine($"  Portfolio Return:      {portfolio.ExpectedReturn:P2}");
        Console.WriteLine();
        Console.WriteLine($"  Average Stock Risk:    {avgRisk:P2}");
        Console.WriteLine($"  Portfolio Risk:        {portfolio.Risk:P2} (sqrt(w' S w) with the window covariance)");
        Console.WriteLine();
        Console.WriteLine($"  Average Return/Risk:   {avgSharpe:F2}");
        Console.WriteLine($"  Portfolio Return/Risk: {portfolio.SharpeRatio:F2}");
        Console.WriteLine();
    }

    /// <summary>
    /// Display the one-year range implied by the window figures (expected return +/- one standard deviation).
    /// </summary>
    private static void DisplayOneYearRange(
        HybridSolver.Solution<PortfolioSolver.PortfolioSolution> solution,
        MarketStats market,
        double budget)
    {
        var portfolio = solution.Result;
        double risk = portfolio.Risk;

        double expectedGain = portfolio.TotalValue * portfolio.ExpectedReturn;
        double potentialRange = portfolio.TotalValue * risk;
        double upper = portfolio.TotalValue + expectedGain + potentialRange;
        double lower = portfolio.TotalValue + expectedGain - potentialRange;

        Console.WriteLine("╔══════════════════════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║                    ONE-YEAR RANGE FROM THE WINDOW FIGURES                    ║");
        Console.WriteLine("╚══════════════════════════════════════════════════════════════════════════════╝");
        Console.WriteLine();
        Console.WriteLine($"  Initial Investment:    ${budget:N2}");
        Console.WriteLine($"  Expected Return:       ${expectedGain:N2} ({portfolio.ExpectedReturn:+0.00%;-0.00%} a year, {market.WindowFrom}..{market.WindowTo} average)");
        Console.WriteLine($"  Risk (with corr.):     {risk:P2}");
        Console.WriteLine();
        Console.WriteLine("  ONE STANDARD DEVIATION EITHER SIDE (about 68% of years if returns were normal):");
        Console.WriteLine($"  • Upper:               ${upper:N2} ({(upper - budget) / budget:+0.00%;-0.00%})");
        Console.WriteLine($"  • Expected:            ${portfolio.TotalValue + expectedGain:N2} ({expectedGain / budget:+0.00%;-0.00%})");
        Console.WriteLine($"  • Lower:               ${lower:N2} ({(lower - budget) / budget:+0.00%;-0.00%})");
        Console.WriteLine();
    }

    /// <summary>
    /// Display how the chosen portfolio and an equal-weight portfolio did in the hold-out year.
    /// </summary>
    private static void DisplayHoldout(
        HybridSolver.Solution<PortfolioSolver.PortfolioSolution> solution,
        MarketStats market,
        PortfolioTypes.Asset[] stocks)
    {
        var chosen = Normalise(solution.Result.Allocations.Select(a => (a.Asset.Symbol, a.Value)));
        var equal = Normalise(stocks.Select(s => (s.Symbol, 1.0)));
        string buyDate = chosen.Select(w => TryAsset(market, w.Symbol)?.BuyDate).FirstOrDefault(d => d is not null) ?? market.WindowTo;

        Console.WriteLine("╔══════════════════════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║                          HOLD-OUT CHECK                                      ║");
        Console.WriteLine("╚══════════════════════════════════════════════════════════════════════════════╝");
        Console.WriteLine();
        Console.WriteLine($"  {"",-28} {"Expected",9} {"Risk",13} {"Realised",10}");
        Console.WriteLine($"  {"",-28} {"(window)",9} {"(with corr.)",13} {"(hold-out)",10}");
        Console.WriteLine($"  {"Optimised portfolio",-28} {Pct(ExpectedReturn(market, chosen)),9} {Pct(solution.Result.Risk),13} {Pct(HoldoutReturn(market, chosen), signed: true),10}");
        Console.WriteLine($"  {"Equal weight, " + stocks.Length + " symbols",-28} {Pct(ExpectedReturn(market, equal)),9} {Pct(VolatilityWithCorrelations(market, equal)),13} {Pct(HoldoutReturn(market, equal), signed: true),10}");
        Console.WriteLine();
        Console.WriteLine($"  Hold-out {market.HoldoutFrom}..{market.HoldoutTo}: bought at the {buyDate} adjusted close, held without rebalancing.");
        Console.WriteLine($"  Risk is sqrt(w' S w) with the {market.WindowFrom}..{market.WindowTo} covariance.");
        Console.WriteLine("  Past performance over one fixed window; not investment advice.");
        Console.WriteLine();
    }
}
