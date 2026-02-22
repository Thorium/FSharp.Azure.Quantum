// ==============================================================================
// Investment Portfolio Optimization
// ==============================================================================
// Portfolio optimization using HybridSolver with quantum-ready optimization
// to balance risk and return across multiple assets. Compares per-asset
// allocation, contribution, and risk metrics.
//
// Usage:
//   dotnet fsi InvestmentPortfolio.fsx                                   (defaults)
//   dotnet fsi InvestmentPortfolio.fsx -- --help                         (show options)
//   dotnet fsi InvestmentPortfolio.fsx -- --symbols AAPL,NVDA,MSFT       (select stocks)
//   dotnet fsi InvestmentPortfolio.fsx -- --input custom-stocks.csv
//   dotnet fsi InvestmentPortfolio.fsx -- --live --budget 50000
//   dotnet fsi InvestmentPortfolio.fsx -- --quiet --output results.json --csv out.csv
//
// References:
//   [1] Markowitz, "Portfolio Selection", J. Finance 7(1), 77-91 (1952)
//   [2] Orus et al., "Quantum computing for finance", Rev. Mod. Phys. 91 (2019)
//   [3] https://en.wikipedia.org/wiki/Modern_portfolio_theory
// ==============================================================================

//#r "nuget: FSharp.Azure.Quantum"
#r "nuget: Microsoft.Extensions.Logging.Abstractions, 10.0.0"
#r "../../src/FSharp.Azure.Quantum/bin/Debug/net10.0/FSharp.Azure.Quantum.dll"

#load "../_common/Cli.fs"
#load "../_common/Data.fs"
#load "../_common/Reporting.fs"

open System
open System.Net.Http
open System.IO
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.Classical
open FSharp.Azure.Quantum.Data
open FSharp.Azure.Quantum.Examples.Common

// ==============================================================================
// CLI ARGUMENT PARSING
// ==============================================================================

let argv = fsi.CommandLineArgs |> Array.skip 1
let args = Cli.parse argv

Cli.exitIfHelp
    "InvestmentPortfolio.fsx"
    "Portfolio optimization using HybridSolver with quantum-ready optimization."
    [ { Cli.OptionSpec.Name = "symbols";  Description = "Comma-separated stock symbols to include"; Default = None }
      { Cli.OptionSpec.Name = "input";    Description = "CSV file with custom stock definitions";   Default = None }
      { Cli.OptionSpec.Name = "budget";   Description = "Investment budget in dollars";              Default = Some "100000" }
      { Cli.OptionSpec.Name = "live";     Description = "Fetch live data from Yahoo Finance";        Default = None }
      { Cli.OptionSpec.Name = "output";   Description = "Write results to JSON file";                Default = None }
      { Cli.OptionSpec.Name = "csv";      Description = "Write results to CSV file";                 Default = None }
      { Cli.OptionSpec.Name = "quiet";    Description = "Suppress informational output";             Default = None } ]
    args

let quiet = Cli.hasFlag "quiet" args
let outputPath = Cli.tryGet "output" args
let csvPath = Cli.tryGet "csv" args
let budget = Cli.getFloatOr "budget" 100000.0 args

// ==============================================================================
// DOMAIN TYPES
// ==============================================================================

/// A stock with historical performance data
type StockInfo = {
    Symbol: string
    Name: string
    ExpectedReturn: float
    Volatility: float
    Price: float
}

/// Per-stock result from portfolio optimization
type StockResult = {
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

let private presetAapl  = { Symbol = "AAPL";  Name = "Apple Inc.";             ExpectedReturn = 0.18; Volatility = 0.22; Price = 175.00 }
let private presetMsft  = { Symbol = "MSFT";  Name = "Microsoft Corp.";        ExpectedReturn = 0.22; Volatility = 0.25; Price = 380.00 }
let private presetGoogl = { Symbol = "GOOGL"; Name = "Alphabet Inc.";          ExpectedReturn = 0.16; Volatility = 0.28; Price = 140.00 }
let private presetAmzn  = { Symbol = "AMZN";  Name = "Amazon.com Inc.";        ExpectedReturn = 0.24; Volatility = 0.32; Price = 155.00 }
let private presetNvda  = { Symbol = "NVDA";  Name = "NVIDIA Corp.";           ExpectedReturn = 0.35; Volatility = 0.45; Price = 485.00 }
let private presetMeta  = { Symbol = "META";  Name = "Meta Platforms Inc.";    ExpectedReturn = 0.28; Volatility = 0.38; Price = 350.00 }
let private presetTsla  = { Symbol = "TSLA";  Name = "Tesla Inc.";             ExpectedReturn = 0.30; Volatility = 0.55; Price = 245.00 }
let private presetAmd   = { Symbol = "AMD";   Name = "Advanced Micro Devices"; ExpectedReturn = 0.26; Volatility = 0.42; Price = 125.00 }

let private builtInStocks =
    [ presetAapl; presetMsft; presetGoogl; presetAmzn; presetNvda; presetMeta; presetTsla; presetAmd ]
    |> List.map (fun s -> s.Symbol.ToUpperInvariant(), s)
    |> Map.ofList

// ==============================================================================
// CSV LOADING
// ==============================================================================

let private loadStocksFromCsv (filePath: string) : StockInfo list =
    let resolved = Data.resolveRelative __SOURCE_DIRECTORY__ filePath
    let rows, errors = Data.readCsvWithHeaderWithErrors resolved
    if not (List.isEmpty errors) then
        eprintfn "WARNING: CSV parse errors in %s:" filePath
        errors |> List.iter (eprintfn "  %s")
    if rows.IsEmpty then failwithf "No valid rows in CSV %s" filePath
    rows |> List.mapi (fun i row ->
        let get key = row.Values |> Map.tryFind key |> Option.defaultValue ""
        match get "preset" with
        | p when not (String.IsNullOrWhiteSpace p) ->
            match builtInStocks |> Map.tryFind (p.Trim().ToUpperInvariant()) with
            | Some s -> s
            | None -> failwithf "Unknown preset '%s' in CSV row %d" p (i + 1)
        | _ ->
            { Symbol         = let s = get "symbol" in if s = "" then failwithf "Missing symbol in CSV row %d" (i + 1) else s.ToUpperInvariant()
              Name           = let n = get "name" in if n = "" then get "symbol" else n
              ExpectedReturn = get "expected_return" |> fun s -> match Double.TryParse s with true, v -> v | _ -> 0.15
              Volatility     = get "volatility"      |> fun s -> match Double.TryParse s with true, v -> v | _ -> 0.25
              Price          = get "price"            |> fun s -> match Double.TryParse s with true, v -> v | _ -> 100.0 })

// ==============================================================================
// STOCK SELECTION
// ==============================================================================

let selectedStocks =
    let base' =
        match Cli.tryGet "input" args with
        | Some csvFile -> loadStocksFromCsv csvFile
        | None -> builtInStocks |> Map.toList |> List.map snd

    match Cli.getCommaSeparated "symbols" args with
    | [] -> base'
    | filter ->
        let filterSet = filter |> List.map (fun s -> s.ToUpperInvariant()) |> Set.ofList
        base' |> List.filter (fun s -> filterSet.Contains(s.Symbol.ToUpperInvariant()))

if selectedStocks.IsEmpty then
    eprintfn "ERROR: No stocks selected. Check --symbols filter or --input CSV."
    exit 1

// ==============================================================================
// LIVE DATA SUPPORT
// ==============================================================================

let liveDataEnabled =
    Cli.hasFlag "live" args
    || (match Environment.GetEnvironmentVariable("INVESTMENTPORTFOLIO_LIVE_DATA") with
        | null -> false
        | s ->
            match s.Trim().ToLowerInvariant() with
            | "1" | "true" | "yes" -> true
            | _ -> false)

let private tryLoadLiveStock (httpClient: HttpClient) (cacheDir: string) (stock: StockInfo) : StockInfo option =
    let req : FinancialData.YahooHistoryRequest = {
        Symbol = stock.Symbol
        Range = FinancialData.YahooHistoryRange.TwoYears
        Interval = FinancialData.YahooHistoryInterval.OneDay
        IncludeAdjustedClose = true
        CacheDirectory = Some cacheDir
        CacheTtl = TimeSpan.FromHours 6.0
    }
    match FinancialData.fetchYahooHistory httpClient req with
    | Error _ -> None
    | Ok series ->
        let returns = FinancialData.calculateReturns series
        let expectedReturn = FinancialData.calculateExpectedReturn returns 252.0
        let volatility = FinancialData.calculateVolatility returns 252.0
        match FinancialData.tryGetLatestPrice series with
        | None -> None
        | Some price ->
            Some { stock with ExpectedReturn = expectedReturn; Volatility = volatility; Price = price }

let stocks =
    if not liveDataEnabled then
        if not quiet then printfn "Using static stock data (use --live for Yahoo Finance)"
        selectedStocks
    else
        let cacheDir = Path.Combine(__SOURCE_DIRECTORY__, "output", "yahoo-cache")
        let _ = Directory.CreateDirectory(cacheDir) |> ignore
        use httpClient = new HttpClient()
        if not quiet then
            printfn "Fetching live data from Yahoo Finance..."
            printfn "  Cache: %s" cacheDir
        let live = selectedStocks |> List.choose (fun s -> tryLoadLiveStock httpClient cacheDir s)
        if live.Length = selectedStocks.Length then live
        else
            if not quiet then printfn "  Live fetch incomplete; falling back to static values"
            selectedStocks

// ==============================================================================
// PORTFOLIO OPTIMIZATION
// ==============================================================================

if not quiet then
    printfn "Optimizing portfolio: %d stocks, budget $%s"
        stocks.Length (budget.ToString("N0"))
    printfn ""

let (results, solverMethod, portfolioReturn, portfolioRisk, portfolioSharpe) =
    let toAsset (s: StockInfo) : PortfolioSolver.Asset =
        { Symbol = s.Symbol; ExpectedReturn = s.ExpectedReturn; Risk = s.Volatility; Price = s.Price }
    let assets = stocks |> List.map toAsset
    let constraints : PortfolioSolver.Constraints =
        { Budget = budget; MinHolding = 0.0; MaxHolding = budget }

    match HybridSolver.solvePortfolio assets constraints None None None with
    | Ok solution ->
        let method = sprintf "%A" solution.Method
        let pReturn = solution.Result.ExpectedReturn
        let pRisk = solution.Result.Risk
        let pSharpe = solution.Result.SharpeRatio
        let totalValue = solution.Result.TotalValue

        let stockResults =
            stocks |> List.map (fun stock ->
                let alloc =
                    solution.Result.Allocations
                    |> List.tryFind (fun a -> a.Asset.Symbol = stock.Symbol)
                let shares = alloc |> Option.map (fun a -> a.Shares) |> Option.defaultValue 0.0
                let value = alloc |> Option.map (fun a -> a.Value) |> Option.defaultValue 0.0
                let pct = if totalValue > 0.0 then value / totalValue * 100.0 else 0.0
                let sharpe = if stock.Volatility > 0.0 then stock.ExpectedReturn / stock.Volatility else 0.0
                { Stock = stock
                  Shares = shares
                  Value = value
                  PctOfPortfolio = pct
                  SharpeRatio = sharpe
                  PortfolioReturn = pReturn
                  PortfolioRisk = pRisk
                  PortfolioSharpe = pSharpe
                  SolverMethod = method
                  HasOptimizationFailure = false })
        (stockResults, method, pReturn, pRisk, pSharpe)

    | Error err ->
        if not quiet then eprintfn "Optimization failed: %A" err
        let failResults =
            stocks |> List.map (fun stock ->
                let sharpe = if stock.Volatility > 0.0 then stock.ExpectedReturn / stock.Volatility else 0.0
                { Stock = stock; Shares = 0.0; Value = 0.0; PctOfPortfolio = 0.0
                  SharpeRatio = sharpe; PortfolioReturn = 0.0; PortfolioRisk = 0.0
                  PortfolioSharpe = 0.0; SolverMethod = "Error"; HasOptimizationFailure = true })
        (failResults, "Error", 0.0, 0.0, 0.0)

// Sort: highest allocation value first
let sortedResults = results |> List.sortByDescending (fun r -> r.Value)

// ==============================================================================
// COMPARISON TABLE (unconditional)
// ==============================================================================

let printTable () =
    let divider = String('-', 106)
    printfn ""
    printfn "  Portfolio Allocation (sorted by value, budget $%s)" (budget.ToString("N0"))
    printfn "  %s" divider
    printfn "  %-6s %-22s %6s %6s %8s %10s %7s %7s %8s"
        "Symbol" "Name" "Return" "Vol" "Sharpe" "Value" "Shares" "Pct" "Status"
    printfn "  %s" divider
    for r in sortedResults do
        let status = if r.HasOptimizationFailure then "FAIL" else "OK"
        printfn "  %-6s %-22s %5.1f%% %5.1f%% %8.2f $%9s %7.2f %5.1f%% %8s"
            r.Stock.Symbol
            (if r.Stock.Name.Length > 22 then r.Stock.Name.[..21] else r.Stock.Name)
            (r.Stock.ExpectedReturn * 100.0)
            (r.Stock.Volatility * 100.0)
            r.SharpeRatio
            (r.Value.ToString("N0"))
            r.Shares
            r.PctOfPortfolio
            status
    printfn "  %s" divider
    printfn ""
    printfn "  Portfolio: Return=%.2f%%  Risk=%.2f%%  Sharpe=%.2f  Method=%s"
        (portfolioReturn * 100.0) (portfolioRisk * 100.0) portfolioSharpe solverMethod

printTable ()

// ==============================================================================
// STRUCTURED OUTPUT (JSON / CSV)
// ==============================================================================

let resultMaps : Map<string, string> list =
    sortedResults
    |> List.map (fun r ->
        [ "symbol",                    r.Stock.Symbol
          "name",                      r.Stock.Name
          "expected_return",           sprintf "%.4f" r.Stock.ExpectedReturn
          "volatility",                sprintf "%.4f" r.Stock.Volatility
          "price",                     sprintf "%.2f" r.Stock.Price
          "shares",                    sprintf "%.4f" r.Shares
          "value",                     sprintf "%.2f" r.Value
          "pct_of_portfolio",          sprintf "%.2f" r.PctOfPortfolio
          "sharpe_ratio",              sprintf "%.4f" r.SharpeRatio
          "portfolio_expected_return", sprintf "%.4f" r.PortfolioReturn
          "portfolio_risk",            sprintf "%.4f" r.PortfolioRisk
          "portfolio_sharpe",          sprintf "%.4f" r.PortfolioSharpe
          "solver_method",             r.SolverMethod
          "budget",                    sprintf "%.2f" budget
          "has_optimization_failure",  sprintf "%b" r.HasOptimizationFailure ]
        |> Map.ofList)

match outputPath with
| Some path ->
    Reporting.writeJson path resultMaps
    if not quiet then printfn "\nResults written to %s" path
| None -> ()

match csvPath with
| Some path ->
    let header =
        [ "symbol"; "name"; "expected_return"; "volatility"; "price"
          "shares"; "value"; "pct_of_portfolio"; "sharpe_ratio"
          "portfolio_expected_return"; "portfolio_risk"; "portfolio_sharpe"
          "solver_method"; "budget"; "has_optimization_failure" ]
    let rows =
        resultMaps |> List.map (fun m ->
            header |> List.map (fun h -> m |> Map.tryFind h |> Option.defaultValue ""))
    Reporting.writeCsv path header rows
    if not quiet then printfn "Results written to %s" path
| None -> ()
