// ==============================================================================
// Investment Portfolio Optimization
// ==============================================================================
// Portfolio optimization using HybridSolver with quantum-ready optimization
// to balance risk and return across multiple assets. Compares per-asset
// allocation, contribution, and risk metrics.
//
// Figures: annual return, volatility and correlations estimated from Yahoo
// Finance daily adjusted closes 2019-2023 (data/market-stats-2019-2023.json);
// the buy price is the 2023-12-29 adjusted close and 2024 is the hold-out year.
// --live recomputes the same figures from Yahoo Finance for any symbols/dates.
//
// Usage:
//   dotnet fsi InvestmentPortfolio.fsx                                   (defaults)
//   dotnet fsi InvestmentPortfolio.fsx -- --help                         (show options)
//   dotnet fsi InvestmentPortfolio.fsx -- --symbols AAPL,NVDA,MSFT       (select stocks)
//   dotnet fsi InvestmentPortfolio.fsx -- --input custom-stocks.csv
//   dotnet fsi InvestmentPortfolio.fsx -- --live --budget 50000
//   dotnet fsi InvestmentPortfolio.fsx -- --live --symbols KO,PEP,JNJ --from 2014-01-01 --to 2018-12-31
//   dotnet fsi InvestmentPortfolio.fsx -- --quiet --output results.json --csv out.csv
//
// References:
//   [1] Markowitz, "Portfolio Selection", J. Finance 7(1), 77-91 (1952)
//   [2] Orus et al., "Quantum computing for finance", Rev. Mod. Phys. 91 (2019)
//   [3] https://en.wikipedia.org/wiki/Modern_portfolio_theory
// ==============================================================================

#r "nuget: Microsoft.Extensions.Logging.Abstractions, 10.0.0"
// The library comes from NuGet; `dotnet fsi --define:LOCAL_BUILD <script>` uses the repo's Debug build.
#if LOCAL_BUILD
#r "../../src/FSharp.Azure.Quantum/bin/Debug/net10.0/FSharp.Azure.Quantum.dll"
#else
#r "nuget: FSharp.Azure.Quantum"
#endif

#load "../_common/Cli.fs"
#load "../_common/Data.fs"
#load "../_common/Reporting.fs"
#load "_marketData.fsx"

open System
open System.Globalization
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.Classical
open FSharp.Azure.Quantum.Examples.Common
open _marketData

// ==============================================================================
// CLI ARGUMENT PARSING
// ==============================================================================

let argv = fsi.CommandLineArgs |> Array.skip 1
let args = Cli.parse argv

Cli.exitIfHelp
    "InvestmentPortfolio.fsx"
    "Portfolio optimization using HybridSolver with quantum-ready optimization."
    [
        {
            Cli.OptionSpec.Name = "symbols"
            Description = "Comma-separated symbols (any Yahoo ticker with --live)"
            Default = Some "AAPL,MSFT,GOOGL,AMZN,NVDA,META,TSLA,AMD"
        }
        {
            Cli.OptionSpec.Name = "input"
            Description = "CSV: symbol[,name,expected_return,volatility,price] or preset"
            Default = None
        }
        {
            Cli.OptionSpec.Name = "budget"
            Description = "Investment budget in dollars"
            Default = Some "100000"
        }
        {
            Cli.OptionSpec.Name = "live"
            Description = "Recompute the figures from Yahoo Finance adjusted closes"
            Default = None
        }
        {
            Cli.OptionSpec.Name = "from"
            Description = "Estimation window start with --live (yyyy-MM-dd)"
            Default = Some "2019-01-01"
        }
        {
            Cli.OptionSpec.Name = "to"
            Description = "Estimation window end with --live; the next year is the hold-out"
            Default = Some "2023-12-31"
        }
        {
            Cli.OptionSpec.Name = "output"
            Description = "Write results to JSON file"
            Default = None
        }
        {
            Cli.OptionSpec.Name = "csv"
            Description = "Write results to CSV file"
            Default = None
        }
        {
            Cli.OptionSpec.Name = "quiet"
            Description = "Suppress informational output"
            Default = None
        }
    ]
    args

let quiet = Cli.hasFlag "quiet" args
let outputPath = Cli.tryGet "output" args
let csvPath = Cli.tryGet "csv" args
let budget = Cli.getFloatOr "budget" 100000.0 args

// ==============================================================================
// DOMAIN TYPES
// ==============================================================================

/// A stock with historical performance data
type StockInfo =
    {
        Symbol: string
        Name: string
        ExpectedReturn: float
        Volatility: float
        Price: float
    }

/// Per-stock result from portfolio optimization
type StockResult =
    {
        Stock: StockInfo
        Shares: float
        Value: float
        PctOfPortfolio: float
        SharpeRatio: float
        PortfolioReturn: float
        PortfolioRisk: float
        PortfolioSharpe: float
        SolverMethod: string
        HasOptimizationFailure: bool
    }

// ==============================================================================
// BUILT-IN STOCK PRESETS
// ==============================================================================
// Symbols only: their figures come from the bundled statistics or from --live.

let private presetSymbols =
    [ "AAPL"; "MSFT"; "GOOGL"; "AMZN"; "NVDA"; "META"; "TSLA"; "AMD" ]

/// A stock to include; Figures = (expected return, volatility, price) when the CSV supplies them.
type private StockSpec =
    {
        Symbol: string
        Name: string option
        Figures: (float * float * float) option
    }

// ==============================================================================
// CSV LOADING
// ==============================================================================

let private tryParseInvariant (s: string) =
    match Double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture) with
    | true, v -> Some v
    | _ -> None

let private loadSpecsFromCsv (filePath: string) : StockSpec list =
    let resolved = Data.resolveRelative __SOURCE_DIRECTORY__ filePath
    let rows, errors = Data.readCsvWithHeaderWithErrors resolved

    if not (List.isEmpty errors) then
        eprintfn "WARNING: CSV parse errors in %s:" filePath
        errors |> List.iter (eprintfn "  %s")

    if rows.IsEmpty then
        failwithf "No valid rows in CSV %s" filePath

    rows
    |> List.mapi (fun i row ->
        let get key =
            row.Values |> Map.tryFind key |> Option.defaultValue ""

        match get "preset" with
        | p when not (String.IsNullOrWhiteSpace p) ->
            {
                Symbol = p.Trim().ToUpperInvariant()
                Name = None
                Figures = None
            }
        | _ ->
            let symbol = get "symbol"

            if symbol = "" then
                failwithf "Missing symbol in CSV row %d" (i + 1)

            let figures =
                match get "expected_return", get "volatility", get "price" with
                | "", "", "" -> None
                | r, v, p ->
                    match tryParseInvariant r, tryParseInvariant v, tryParseInvariant p with
                    | Some r, Some v, Some p -> Some(r, v, p)
                    | _ ->
                        failwithf
                            "CSV row %d (%s): give all of expected_return, volatility, price, or none of them"
                            (i + 1)
                            symbol

            {
                Symbol = symbol.ToUpperInvariant()
                Name =
                    (match get "name" with
                     | "" -> None
                     | n -> Some n)
                Figures = figures
            })

// ==============================================================================
// STOCK SELECTION
// ==============================================================================

let private selectedSpecs =
    let symbolsArg =
        Cli.getCommaSeparated "symbols" args |> List.map (fun s -> s.ToUpperInvariant())

    match Cli.tryGet "input" args with
    | Some csvFile ->
        let specs = loadSpecsFromCsv csvFile

        match symbolsArg with
        | [] -> specs
        | filter -> specs |> List.filter (fun s -> List.contains s.Symbol filter)
    | None ->
        (if symbolsArg.IsEmpty then presetSymbols else symbolsArg)
        |> List.map (fun s ->
            {
                Symbol = s
                Name = None
                Figures = None
            })

if selectedSpecs.IsEmpty then
    eprintfn "ERROR: No stocks selected. Check --symbols filter or --input CSV."
    exit 1

// ==============================================================================
// MARKET FIGURES (bundled, or --live from Yahoo Finance)
// ==============================================================================

let liveDataEnabled =
    Cli.hasFlag "live" args
    || (match Environment.GetEnvironmentVariable("INVESTMENTPORTFOLIO_LIVE_DATA") with
        | null -> false
        | s ->
            match s.Trim().ToLowerInvariant() with
            | "1"
            | "true"
            | "yes" -> true
            | _ -> false)

let private dateArg (name: string) (fallback: DateTime) =
    match Cli.tryGet name args with
    | None -> fallback
    | Some s ->
        match MarketData.tryParseIsoDate s with
        | Some d when liveDataEnabled -> d
        | Some _ ->
            eprintfn "ERROR: --%s needs --live; the bundled figures cover 2019-01-01..2023-12-31." name
            exit 1
        | None ->
            eprintfn "ERROR: --%s must be yyyy-MM-dd, got '%s'" name s
            exit 1

let windowFrom = dateArg "from" MarketData.defaultWindowFrom
let windowTo = dateArg "to" MarketData.defaultWindowTo

let marketStats, liveUsed =
    let needed =
        selectedSpecs
        |> List.filter (fun s -> s.Figures.IsNone || liveDataEnabled)
        |> List.map (fun s -> s.Symbol)

    let info (msg: string) =
        if not quiet then
            printfn "%s" msg

    if liveDataEnabled && not needed.IsEmpty then
        match MarketData.computeFromYahoo info needed windowFrom windowTo None with
        | Ok(stats, dropped) ->
            for (s, reason) in dropped do
                printfn "  Dropped %s: %s" s reason

            stats, true
        | Error e ->
            eprintfn "Live fetch failed (%s); using the bundled figures." e
            MarketData.loadBundled (), false
    else
        MarketData.loadBundled (), false

let stocks =
    selectedSpecs
    |> List.choose (fun spec ->
        match spec.Figures, MarketData.tryAsset marketStats spec.Symbol with
        | Some(r, v, p), _ ->
            Some
                {
                    Symbol = spec.Symbol
                    Name = spec.Name |> Option.defaultValue spec.Symbol
                    ExpectedReturn = r
                    Volatility = v
                    Price = p
                }
        | None, Some a ->
            Some
                {
                    Symbol = a.Symbol
                    Name = spec.Name |> Option.defaultValue a.Name
                    ExpectedReturn = a.AnnualReturn
                    Volatility = a.AnnualVolatility
                    Price = a.BuyPrice
                }
        | None, None ->
            eprintfn "  Skipping %s: no figures for it (not in the bundled data; try --live)" spec.Symbol
            None)

if stocks.IsEmpty then
    eprintfn "ERROR: No stocks with figures."
    exit 1

if not quiet then
    printfn "%s" (MarketData.describeSource marketStats liveUsed)

// ==============================================================================
// PORTFOLIO OPTIMIZATION
// ==============================================================================

if not quiet then
    printfn "Optimizing portfolio: %d stocks, budget $%s" stocks.Length (budget.ToString "N0")
    printfn ""

/// Covariance of the selected stocks from the window correlations; None when a symbol has none.
let portfolioCovariance =
    MarketData.covariance marketStats (stocks |> List.map (fun s -> s.Symbol, s.Volatility))

let (results, solverMethod, portfolioReturn, portfolioRisk, portfolioSharpe) =
    let toAsset (s: StockInfo) : PortfolioSolver.Asset =
        {
            Symbol = s.Symbol
            ExpectedReturn = s.ExpectedReturn
            Risk = s.Volatility
            Price = s.Price
        }

    let assets = stocks |> List.map toAsset

    let constraints: PortfolioSolver.Constraints =
        {
            Budget = budget
            MinHolding = 0.0
            MaxHolding = budget
        }

    let solving =
        match portfolioCovariance with
        | Some sigma ->
            HybridSolver.solvePortfolioWithCovarianceAsync
                assets
                sigma
                constraints
                None
                None
                None
                None
                System.Threading.CancellationToken.None
        | None ->
            HybridSolver.solvePortfolioAsync assets constraints None None None System.Threading.CancellationToken.None

    // Script top level: wait for the solver here.
    let solved = solving |> Async.AwaitTask |> Async.RunSynchronously

    match solved with
    | Ok solution ->
        let method = $"%A{solution.Method}"
        let pReturn = solution.Result.ExpectedReturn
        let pRisk = solution.Result.Risk
        let pSharpe = solution.Result.SharpeRatio
        let totalValue = solution.Result.TotalValue

        let stockResults =
            stocks
            |> List.map (fun stock ->
                let alloc =
                    solution.Result.Allocations
                    |> List.tryFind (fun a -> a.Asset.Symbol = stock.Symbol)

                let shares = alloc |> Option.map (fun a -> a.Shares) |> Option.defaultValue 0.0
                let value = alloc |> Option.map (fun a -> a.Value) |> Option.defaultValue 0.0
                let pct = if totalValue > 0.0 then value / totalValue * 100.0 else 0.0
                // Sharpe ratio = (expected return - risk-free rate) / volatility.
                // The EXCESS return over a risk-free asset earns the risk premium;
                // omitting r_f (assuming 0) overstates every Sharpe ratio.
                let riskFreeRate = 0.02 // annualized; ~short-term T-bill proxy

                let sharpe =
                    if stock.Volatility > 0.0 then
                        (stock.ExpectedReturn - riskFreeRate) / stock.Volatility
                    else
                        0.0

                {
                    Stock = stock
                    Shares = shares
                    Value = value
                    PctOfPortfolio = pct
                    SharpeRatio = sharpe
                    PortfolioReturn = pReturn
                    PortfolioRisk = pRisk
                    PortfolioSharpe = pSharpe
                    SolverMethod = method
                    HasOptimizationFailure = false
                })

        (stockResults, method, pReturn, pRisk, pSharpe)

    | Error err ->
        if not quiet then
            eprintfn "Optimization failed: %A" err

        let failResults =
            stocks
            |> List.map (fun stock ->
                // Sharpe ratio = (expected return - risk-free rate) / volatility.
                // The EXCESS return over a risk-free asset earns the risk premium;
                // omitting r_f (assuming 0) overstates every Sharpe ratio.
                let riskFreeRate = 0.02 // annualized; ~short-term T-bill proxy

                let sharpe =
                    if stock.Volatility > 0.0 then
                        (stock.ExpectedReturn - riskFreeRate) / stock.Volatility
                    else
                        0.0

                {
                    Stock = stock
                    Shares = 0.0
                    Value = 0.0
                    PctOfPortfolio = 0.0
                    SharpeRatio = sharpe
                    PortfolioReturn = 0.0
                    PortfolioRisk = 0.0
                    PortfolioSharpe = 0.0
                    SolverMethod = "Error"
                    HasOptimizationFailure = true
                })

        (failResults, "Error", 0.0, 0.0, 0.0)

// Sort: highest allocation value first
let sortedResults = results |> List.sortByDescending (fun r -> r.Value)

let chosenWeights = sortedResults |> List.map (fun r -> r.Stock.Symbol, r.Value)
let equalWeights = stocks |> List.map (fun s -> s.Symbol, 1.0)

let private fmtOption (x: float option) =
    x
    |> Option.map (fun v -> v.ToString("F4", CultureInfo.InvariantCulture))
    |> Option.defaultValue ""

// ==============================================================================
// COMPARISON TABLE (unconditional)
// ==============================================================================

let printTable () =
    let divider = String('-', 106)
    printfn ""
    printfn "  Portfolio Allocation (sorted by value, budget $%s)" (budget.ToString "N0")
    printfn "  %s" divider

    printfn
        "  %-6s %-22s %6s %6s %8s %10s %7s %7s %8s"
        "Symbol"
        "Name"
        "Return"
        "Vol"
        "Sharpe"
        "Value"
        "Shares"
        "Pct"
        "Status"

    printfn "  %s" divider

    for r in sortedResults do
        let status = if r.HasOptimizationFailure then "FAIL" else "OK"

        printfn
            "  %-6s %-22s %5.1f%% %5.1f%% %8.2f $%9s %7.2f %5.1f%% %8s"
            r.Stock.Symbol
            (if r.Stock.Name.Length > 22 then
                 r.Stock.Name.[..21]
             else
                 r.Stock.Name)
            (r.Stock.ExpectedReturn * 100.0)
            (r.Stock.Volatility * 100.0)
            r.SharpeRatio
            (r.Value.ToString "N0")
            r.Shares
            r.PctOfPortfolio
            status

    printfn "  %s" divider
    printfn ""

    printfn
        "  Portfolio: Return=%.2f%%  Risk=%.2f%%  Sharpe=%.2f  Method=%s"
        (portfolioReturn * 100.0)
        (portfolioRisk * 100.0)
        portfolioSharpe
        solverMethod

    if solverMethod <> "Error" then
        MarketData.printComparison
            marketStats
            chosenWeights
            (stocks |> List.map (fun s -> s.Symbol))
            (portfolioCovariance |> Option.map (fun _ -> portfolioRisk))

printTable ()

// ==============================================================================
// STRUCTURED OUTPUT (JSON / CSV)
// ==============================================================================

let resultMaps: Map<string, string> list =
    sortedResults
    |> List.map (fun r ->
        [
            "symbol", r.Stock.Symbol
            "name", r.Stock.Name
            "expected_return", $"%.4f{r.Stock.ExpectedReturn}"
            "volatility", $"%.4f{r.Stock.Volatility}"
            "price", $"%.2f{r.Stock.Price}"
            "shares", $"%.4f{r.Shares}"
            "value", $"%.2f{r.Value}"
            "pct_of_portfolio", $"%.2f{r.PctOfPortfolio}"
            "sharpe_ratio", $"%.4f{r.SharpeRatio}"
            "portfolio_expected_return", $"%.4f{r.PortfolioReturn}"
            "portfolio_risk", $"%.4f{r.PortfolioRisk}"
            "portfolio_sharpe", $"%.4f{r.PortfolioSharpe}"
            "solver_method", r.SolverMethod
            "budget", $"%.2f{budget}"
            "has_optimization_failure", $"%b{r.HasOptimizationFailure}"
            "holdout_return",
            fmtOption (
                MarketData.tryAsset marketStats r.Stock.Symbol
                |> Option.bind (fun a -> a.HoldoutReturn)
            )
            "portfolio_holdout_return", fmtOption (MarketData.holdoutReturn marketStats chosenWeights)
            "equal_weight_holdout_return", fmtOption (MarketData.holdoutReturn marketStats equalWeights)
            "estimation_window", $"%s{marketStats.WindowFrom}..%s{marketStats.WindowTo}"
            "holdout_window", $"%s{marketStats.HoldoutFrom}..%s{marketStats.HoldoutTo}"
        ]
        |> Map.ofList)

match outputPath with
| Some path ->
    Reporting.writeJson path resultMaps

    if not quiet then
        printfn "\nResults written to %s" path
| None -> ()

match csvPath with
| Some path ->
    let header =
        [
            "symbol"
            "name"
            "expected_return"
            "volatility"
            "price"
            "shares"
            "value"
            "pct_of_portfolio"
            "sharpe_ratio"
            "portfolio_expected_return"
            "portfolio_risk"
            "portfolio_sharpe"
            "solver_method"
            "budget"
            "has_optimization_failure"
            "holdout_return"
            "portfolio_holdout_return"
            "equal_weight_holdout_return"
            "estimation_window"
            "holdout_window"
        ]

    let rows =
        resultMaps
        |> List.map (fun m -> header |> List.map (fun h -> m |> Map.tryFind h |> Option.defaultValue ""))

    Reporting.writeCsv path header rows

    if not quiet then
        printfn "Results written to %s" path
| None -> ()
