// ==============================================================================
// Investment Portfolio - Small Quantum Test
// ==============================================================================
// Direct quantum portfolio optimization using QuantumPortfolioSolver with a
// minimal asset set (3 qubits by default).
// QUBO encoding + QAOA execution for mean-variance portfolio problems.
// One qubit per asset; the local simulator run is capped at 16 assets.
//
// Figures: annual return and volatility estimated from Yahoo Finance daily
// adjusted closes 2019-2023 (data/market-stats-2019-2023.json); the buy price
// is the 2023-12-29 adjusted close and 2024 is the hold-out year.
// --live recomputes the same figures from Yahoo Finance for any symbols/dates.
//
// Usage:
//   dotnet fsi InvestmentPortfolio-Small.fsx                                  (defaults)
//   dotnet fsi InvestmentPortfolio-Small.fsx -- --help                        (show options)
//   dotnet fsi InvestmentPortfolio-Small.fsx -- --symbols AAPL,MSFT           (select stocks)
//   dotnet fsi InvestmentPortfolio-Small.fsx -- --input custom-stocks.csv
//   dotnet fsi InvestmentPortfolio-Small.fsx -- --budget 20000 --risk-aversion 0.7
//   dotnet fsi InvestmentPortfolio-Small.fsx -- --live --symbols KO,PEP,JNJ --from 2014-01-01 --to 2018-12-31
//   dotnet fsi InvestmentPortfolio-Small.fsx -- --quiet --output results.json --csv out.csv
//
// References:
//   [1] Farhi et al., "A Quantum Approximate Optimization Algorithm" (2014)
//   [2] Markowitz, "Portfolio Selection", J. Finance 7(1), 77-91 (1952)
//   [3] https://en.wikipedia.org/wiki/Quantum_approximate_optimization_algorithm
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
open FSharp.Azure.Quantum.Classical
open FSharp.Azure.Quantum.Classical.PortfolioSolver
open FSharp.Azure.Quantum.Quantum
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Backends.LocalBackend
open FSharp.Azure.Quantum.Examples.Common
open _marketData

// ==============================================================================
// CLI ARGUMENT PARSING
// ==============================================================================

let argv = fsi.CommandLineArgs |> Array.skip 1
let args = Cli.parse argv

Cli.exitIfHelp
    "InvestmentPortfolio-Small.fsx"
    "Direct quantum portfolio optimization with QuantumPortfolioSolver (QAOA)."
    [
        {
            Cli.OptionSpec.Name = "symbols"
            Description = "Comma-separated symbols, at most 16 (any Yahoo ticker with --live)"
            Default = Some "AAPL,MSFT,GOOGL"
        }
        {
            Cli.OptionSpec.Name = "input"
            Description = "CSV: symbol[,name,expected_return,risk,price] or preset"
            Default = None
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
            Cli.OptionSpec.Name = "shots"
            Description = "Number of measurement shots"
            Default = Some "1000"
        }
        {
            Cli.OptionSpec.Name = "budget"
            Description = "Investment budget in dollars"
            Default = Some "10000"
        }
        {
            Cli.OptionSpec.Name = "risk-aversion"
            Description = "Risk aversion factor (0.0-1.0)"
            Default = Some "0.5"
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
let shots = Cli.getIntOr "shots" 1000 args
let budget = Cli.getFloatOr "budget" 10000.0 args
let riskAversion = Cli.getFloatOr "risk-aversion" 0.5 args

// ==============================================================================
// DOMAIN TYPES
// ==============================================================================

/// A stock with historical performance data
type StockInfo =
    {
        Symbol: string
        Name: string
        ExpectedReturn: float
        Risk: float
        Price: float
    }

/// Per-stock result from quantum portfolio optimization
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
        BestEnergy: float
        BackendName: string
        SolverElapsedMs: float
        HasQuantumFailure: bool
    }

// ==============================================================================
// BUILT-IN STOCK PRESETS
// ==============================================================================
// Symbols only: their figures come from the bundled statistics or from --live.

let private presetSymbols = [ "AAPL"; "MSFT"; "GOOGL" ]

/// One qubit per asset; larger problems do not fit the local state-vector simulator.
[<Literal>]
let private maxAssets = 16

/// A stock to include; Figures = (expected return, risk, price) when the CSV supplies them.
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
                match get "expected_return", get "risk", get "price" with
                | "", "", "" -> None
                | r, v, p ->
                    match tryParseInvariant r, tryParseInvariant v, tryParseInvariant p with
                    | Some r, Some v, Some p -> Some(r, v, p)
                    | _ ->
                        failwithf
                            "CSV row %d (%s): give all of expected_return, risk, price, or none of them"
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

if selectedSpecs.Length > maxAssets then
    eprintfn
        "ERROR: %d assets need %d qubits; this example simulates at most %d."
        selectedSpecs.Length
        selectedSpecs.Length
        maxAssets

    exit 1

// ==============================================================================
// MARKET FIGURES (bundled, or --live from Yahoo Finance)
// ==============================================================================

let liveDataEnabled = Cli.hasFlag "live" args

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
    let info (msg: string) =
        if not quiet then
            printfn "%s" msg

    if liveDataEnabled then
        match
            MarketData.computeFromYahooAsync
                info
                (selectedSpecs |> List.map (fun s -> s.Symbol))
                windowFrom
                windowTo
                None
            |> Async.AwaitTask
            |> Async.RunSynchronously
        with
        | Ok(stats, dropped) ->
            for (s, reason) in dropped do
                printfn "  Dropped %s: %s" s reason

            stats, true
        | Error e ->
            eprintfn "Live fetch failed (%s); using the bundled figures." e
            MarketData.loadBundled (), false
    else
        MarketData.loadBundled (), false

let selectedStocks =
    selectedSpecs
    |> List.choose (fun spec ->
        match spec.Figures, MarketData.tryAsset marketStats spec.Symbol with
        | Some(r, v, p), _ ->
            Some
                {
                    Symbol = spec.Symbol
                    Name = spec.Name |> Option.defaultValue spec.Symbol
                    ExpectedReturn = r
                    Risk = v
                    Price = p
                }
        | None, Some a ->
            Some
                {
                    Symbol = a.Symbol
                    Name = spec.Name |> Option.defaultValue a.Name
                    ExpectedReturn = a.AnnualReturn
                    Risk = a.AnnualVolatility
                    Price = a.BuyPrice
                }
        | None, None ->
            eprintfn "  Skipping %s: no figures for it (not in the bundled data; try --live)" spec.Symbol
            None)

if selectedStocks.IsEmpty then
    eprintfn "ERROR: No stocks with figures."
    exit 1

if not quiet then
    printfn "%s" (MarketData.describeSource marketStats liveUsed)

// ==============================================================================
// QUANTUM PORTFOLIO OPTIMIZATION
// ==============================================================================

if not quiet then
    printfn
        "Quantum portfolio optimization: %d assets, budget $%s, shots %d, risk-aversion %.2f"
        selectedStocks.Length
        (budget.ToString "N0")
        shots
        riskAversion

    printfn ""

let backend = LocalBackend() :> IQuantumBackend

let toAsset (s: StockInfo) : Asset =
    {
        Symbol = s.Symbol
        ExpectedReturn = s.ExpectedReturn
        Risk = s.Risk
        Price = s.Price
    }

let assets = selectedStocks |> List.map toAsset

let constraints: Constraints =
    {
        Budget = budget
        MinHolding = 0.0
        MaxHolding = budget
    }

let config: QuantumPortfolioSolver.QuantumPortfolioConfig =
    {
        NumShots = shots
        RiskAversion = riskAversion
        InitialParameters = (0.5, 0.5)
    }

/// Covariance of the selected stocks from the window correlations; None when a symbol has none.
let portfolioCovariance =
    MarketData.covariance marketStats (selectedStocks |> List.map (fun s -> s.Symbol, s.Risk))

let solved =
    let run =
        match portfolioCovariance with
        | Some sigma ->
            QuantumPortfolioSolver.solveWithCovarianceAsync
                backend
                assets
                sigma
                constraints
                config
                System.Threading.CancellationToken.None
        | None ->
            QuantumPortfolioSolver.solveAsync backend assets constraints config System.Threading.CancellationToken.None

    run |> Async.AwaitTask |> Async.RunSynchronously

let sortedResults =
    match solved with
    | Ok solution ->
        let totalValue = solution.Allocations |> List.sumBy (fun a -> a.Value)

        let pSharpe =
            if solution.Risk > 0.0 then
                solution.ExpectedReturn / solution.Risk
            else
                0.0

        selectedStocks
        |> List.map (fun stock ->
            let alloc =
                solution.Allocations |> List.tryFind (fun a -> a.Asset.Symbol = stock.Symbol)

            let shares = alloc |> Option.map (fun a -> a.Shares) |> Option.defaultValue 0.0
            let value = alloc |> Option.map (fun a -> a.Value) |> Option.defaultValue 0.0
            let pct = if totalValue > 0.0 then value / totalValue * 100.0 else 0.0
            // Sharpe uses EXCESS return over the risk-free rate, not raw return.
            let riskFreeRate = 0.02 // annualized; ~short-term T-bill proxy

            let sharpe =
                if stock.Risk > 0.0 then
                    (stock.ExpectedReturn - riskFreeRate) / stock.Risk
                else
                    0.0

            {
                Stock = stock
                Shares = shares
                Value = value
                PctOfPortfolio = pct
                SharpeRatio = sharpe
                PortfolioReturn = solution.ExpectedReturn
                PortfolioRisk = solution.Risk
                PortfolioSharpe = pSharpe
                BestEnergy = solution.BestEnergy
                BackendName = solution.BackendName
                SolverElapsedMs = solution.ElapsedMs
                HasQuantumFailure = false
            })
        |> List.sortByDescending (fun r -> r.Value)

    | Error err ->
        if not quiet then
            eprintfn "Quantum optimization failed: %s" err.Message

        selectedStocks
        |> List.map (fun stock ->
            // Sharpe uses EXCESS return over the risk-free rate, not raw return.
            let riskFreeRate = 0.02 // annualized; ~short-term T-bill proxy

            let sharpe =
                if stock.Risk > 0.0 then
                    (stock.ExpectedReturn - riskFreeRate) / stock.Risk
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
                BestEnergy = 0.0
                BackendName = "N/A"
                SolverElapsedMs = 0.0
                HasQuantumFailure = true
            })

let chosenWeights = sortedResults |> List.map (fun r -> r.Stock.Symbol, r.Value)
let equalWeights = selectedStocks |> List.map (fun s -> s.Symbol, 1.0)

let private fmtOption (x: float option) =
    x
    |> Option.map (fun v -> v.ToString("F4", CultureInfo.InvariantCulture))
    |> Option.defaultValue ""

// ==============================================================================
// COMPARISON TABLE (unconditional)
// ==============================================================================

let printTable () =
    let first = sortedResults |> List.tryHead

    let pReturn =
        first |> Option.map (fun r -> r.PortfolioReturn) |> Option.defaultValue 0.0

    let pRisk =
        first |> Option.map (fun r -> r.PortfolioRisk) |> Option.defaultValue 0.0

    let pSharpe =
        first |> Option.map (fun r -> r.PortfolioSharpe) |> Option.defaultValue 0.0

    let energy = first |> Option.map (fun r -> r.BestEnergy) |> Option.defaultValue 0.0

    let backendName =
        first |> Option.map (fun r -> r.BackendName) |> Option.defaultValue "N/A"

    let divider = String('-', 96)
    printfn ""
    printfn "  Quantum Portfolio (budget $%s, shots %d, risk-aversion %.2f)" (budget.ToString "N0") shots riskAversion
    printfn "  %s" divider

    printfn
        "  %-6s %-18s %6s %6s %8s %10s %7s %7s %8s"
        "Symbol"
        "Name"
        "Return"
        "Risk"
        "Sharpe"
        "Value"
        "Shares"
        "Pct"
        "Status"

    printfn "  %s" divider

    for r in sortedResults do
        let status = if r.HasQuantumFailure then "FAIL" else "OK"

        printfn
            "  %-6s %-18s %5.1f%% %5.1f%% %8.2f $%9s %7.2f %5.1f%% %8s"
            r.Stock.Symbol
            (if r.Stock.Name.Length > 18 then
                 r.Stock.Name.[..17]
             else
                 r.Stock.Name)
            (r.Stock.ExpectedReturn * 100.0)
            (r.Stock.Risk * 100.0)
            r.SharpeRatio
            (r.Value.ToString "N0")
            r.Shares
            r.PctOfPortfolio
            status

    printfn "  %s" divider
    printfn ""

    printfn
        "  Portfolio: Return=%.2f%%  Risk=%.2f%%  Sharpe=%.2f  Energy=%.4f  Backend=%s"
        (pReturn * 100.0)
        (pRisk * 100.0)
        pSharpe
        energy
        backendName

    if not (sortedResults |> List.exists (fun r -> r.HasQuantumFailure)) then
        MarketData.printComparison
            marketStats
            chosenWeights
            (selectedStocks |> List.map (fun s -> s.Symbol))
            (match portfolioCovariance, sortedResults with
             | Some _, r :: _ when not r.HasQuantumFailure -> Some r.PortfolioRisk
             | _ -> None)

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
            "risk", $"%.4f{r.Stock.Risk}"
            "price", $"%.2f{r.Stock.Price}"
            "shares", $"%.4f{r.Shares}"
            "value", $"%.2f{r.Value}"
            "pct_of_portfolio", $"%.2f{r.PctOfPortfolio}"
            "sharpe_ratio", $"%.4f{r.SharpeRatio}"
            "portfolio_return", $"%.4f{r.PortfolioReturn}"
            "portfolio_risk", $"%.4f{r.PortfolioRisk}"
            "portfolio_sharpe", $"%.4f{r.PortfolioSharpe}"
            "best_energy", $"%.4f{r.BestEnergy}"
            "backend_name", r.BackendName
            "solver_elapsed_ms", $"%.1f{r.SolverElapsedMs}"
            "budget", $"%.2f{budget}"
            "shots", $"%d{shots}"
            "risk_aversion", $"%.2f{riskAversion}"
            "has_quantum_failure", $"%b{r.HasQuantumFailure}"
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
            "risk"
            "price"
            "shares"
            "value"
            "pct_of_portfolio"
            "sharpe_ratio"
            "portfolio_return"
            "portfolio_risk"
            "portfolio_sharpe"
            "best_energy"
            "backend_name"
            "solver_elapsed_ms"
            "budget"
            "shots"
            "risk_aversion"
            "has_quantum_failure"
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
