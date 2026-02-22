// ==============================================================================
// Investment Portfolio - Small Quantum Test
// ==============================================================================
// Direct quantum portfolio optimization using QuantumPortfolioSolver with a
// minimal asset set that fits within LocalSimulator constraints (<10 qubits).
// QUBO encoding + QAOA execution for mean-variance portfolio problems.
//
// Usage:
//   dotnet fsi InvestmentPortfolio-Small.fsx                                  (defaults)
//   dotnet fsi InvestmentPortfolio-Small.fsx -- --help                        (show options)
//   dotnet fsi InvestmentPortfolio-Small.fsx -- --symbols AAPL,MSFT           (select stocks)
//   dotnet fsi InvestmentPortfolio-Small.fsx -- --input custom-stocks.csv
//   dotnet fsi InvestmentPortfolio-Small.fsx -- --budget 20000 --risk-aversion 0.7
//   dotnet fsi InvestmentPortfolio-Small.fsx -- --quiet --output results.json --csv out.csv
//
// References:
//   [1] Farhi et al., "A Quantum Approximate Optimization Algorithm" (2014)
//   [2] Markowitz, "Portfolio Selection", J. Finance 7(1), 77-91 (1952)
//   [3] https://en.wikipedia.org/wiki/Quantum_approximate_optimization_algorithm
// ==============================================================================

//#r "nuget: FSharp.Azure.Quantum"
#r "nuget: Microsoft.Extensions.Logging.Abstractions, 10.0.0"
#r "../../src/FSharp.Azure.Quantum/bin/Debug/net10.0/FSharp.Azure.Quantum.dll"

#load "../_common/Cli.fs"
#load "../_common/Data.fs"
#load "../_common/Reporting.fs"

open System
open FSharp.Azure.Quantum.Classical
open FSharp.Azure.Quantum.Classical.PortfolioSolver
open FSharp.Azure.Quantum.Quantum
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Backends.LocalBackend
open FSharp.Azure.Quantum.Examples.Common

// ==============================================================================
// CLI ARGUMENT PARSING
// ==============================================================================

let argv = fsi.CommandLineArgs |> Array.skip 1
let args = Cli.parse argv

Cli.exitIfHelp
    "InvestmentPortfolio-Small.fsx"
    "Direct quantum portfolio optimization with QuantumPortfolioSolver (QAOA)."
    [ { Cli.OptionSpec.Name = "symbols";       Description = "Comma-separated stock symbols to include"; Default = None }
      { Cli.OptionSpec.Name = "input";         Description = "CSV file with custom stock definitions";   Default = None }
      { Cli.OptionSpec.Name = "shots";         Description = "Number of measurement shots";               Default = Some "1000" }
      { Cli.OptionSpec.Name = "budget";        Description = "Investment budget in dollars";               Default = Some "10000" }
      { Cli.OptionSpec.Name = "risk-aversion"; Description = "Risk aversion factor (0.0-1.0)";            Default = Some "0.5" }
      { Cli.OptionSpec.Name = "output";        Description = "Write results to JSON file";                 Default = None }
      { Cli.OptionSpec.Name = "csv";           Description = "Write results to CSV file";                  Default = None }
      { Cli.OptionSpec.Name = "quiet";         Description = "Suppress informational output";              Default = None } ]
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
type StockInfo = {
    Symbol: string
    Name: string
    ExpectedReturn: float
    Risk: float
    Price: float
}

/// Per-stock result from quantum portfolio optimization
type StockResult = {
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

let private presetAapl  = { Symbol = "AAPL";  Name = "Apple Inc.";      ExpectedReturn = 0.15; Risk = 0.20; Price = 175.0 }
let private presetMsft  = { Symbol = "MSFT";  Name = "Microsoft Corp."; ExpectedReturn = 0.18; Risk = 0.22; Price = 380.0 }
let private presetGoogl = { Symbol = "GOOGL"; Name = "Alphabet Inc.";   ExpectedReturn = 0.12; Risk = 0.25; Price = 140.0 }

let private builtInStocks =
    [ presetAapl; presetMsft; presetGoogl ]
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
              Risk           = get "risk"            |> fun s -> match Double.TryParse s with true, v -> v | _ -> 0.20
              Price          = get "price"           |> fun s -> match Double.TryParse s with true, v -> v | _ -> 100.0 })

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
// QUANTUM PORTFOLIO OPTIMIZATION
// ==============================================================================

if not quiet then
    printfn "Quantum portfolio optimization: %d assets, budget $%s, shots %d, risk-aversion %.2f"
        selectedStocks.Length (budget.ToString("N0")) shots riskAversion
    printfn ""

let backend = LocalBackend() :> IQuantumBackend

let toAsset (s: StockInfo) : Asset =
    { Symbol = s.Symbol; ExpectedReturn = s.ExpectedReturn; Risk = s.Risk; Price = s.Price }

let assets = selectedStocks |> List.map toAsset
let constraints : Constraints = { Budget = budget; MinHolding = 0.0; MaxHolding = budget }
let config : QuantumPortfolioSolver.QuantumPortfolioConfig =
    { NumShots = shots; RiskAversion = riskAversion; InitialParameters = (0.5, 0.5) }

let sortedResults =
    match QuantumPortfolioSolver.solve backend assets constraints config with
    | Ok solution ->
        let totalValue = solution.Allocations |> List.sumBy (fun a -> a.Value)
        let pSharpe = if solution.Risk > 0.0 then solution.ExpectedReturn / solution.Risk else 0.0

        selectedStocks |> List.map (fun stock ->
            let alloc = solution.Allocations |> List.tryFind (fun a -> a.Asset.Symbol = stock.Symbol)
            let shares = alloc |> Option.map (fun a -> a.Shares) |> Option.defaultValue 0.0
            let value = alloc |> Option.map (fun a -> a.Value) |> Option.defaultValue 0.0
            let pct = if totalValue > 0.0 then value / totalValue * 100.0 else 0.0
            let sharpe = if stock.Risk > 0.0 then stock.ExpectedReturn / stock.Risk else 0.0
            { Stock = stock
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
              HasQuantumFailure = false })
        |> List.sortByDescending (fun r -> r.Value)

    | Error err ->
        if not quiet then eprintfn "Quantum optimization failed: %s" err.Message
        selectedStocks |> List.map (fun stock ->
            let sharpe = if stock.Risk > 0.0 then stock.ExpectedReturn / stock.Risk else 0.0
            { Stock = stock; Shares = 0.0; Value = 0.0; PctOfPortfolio = 0.0
              SharpeRatio = sharpe; PortfolioReturn = 0.0; PortfolioRisk = 0.0
              PortfolioSharpe = 0.0; BestEnergy = 0.0; BackendName = "N/A"
              SolverElapsedMs = 0.0; HasQuantumFailure = true })

// ==============================================================================
// COMPARISON TABLE (unconditional)
// ==============================================================================

let printTable () =
    let first = sortedResults |> List.tryHead
    let pReturn = first |> Option.map (fun r -> r.PortfolioReturn) |> Option.defaultValue 0.0
    let pRisk = first |> Option.map (fun r -> r.PortfolioRisk) |> Option.defaultValue 0.0
    let pSharpe = first |> Option.map (fun r -> r.PortfolioSharpe) |> Option.defaultValue 0.0
    let energy = first |> Option.map (fun r -> r.BestEnergy) |> Option.defaultValue 0.0
    let backendName = first |> Option.map (fun r -> r.BackendName) |> Option.defaultValue "N/A"

    let divider = String('-', 96)
    printfn ""
    printfn "  Quantum Portfolio (budget $%s, shots %d, risk-aversion %.2f)"
        (budget.ToString("N0")) shots riskAversion
    printfn "  %s" divider
    printfn "  %-6s %-18s %6s %6s %8s %10s %7s %7s %8s"
        "Symbol" "Name" "Return" "Risk" "Sharpe" "Value" "Shares" "Pct" "Status"
    printfn "  %s" divider
    for r in sortedResults do
        let status = if r.HasQuantumFailure then "FAIL" else "OK"
        printfn "  %-6s %-18s %5.1f%% %5.1f%% %8.2f $%9s %7.2f %5.1f%% %8s"
            r.Stock.Symbol
            (if r.Stock.Name.Length > 18 then r.Stock.Name.[..17] else r.Stock.Name)
            (r.Stock.ExpectedReturn * 100.0)
            (r.Stock.Risk * 100.0)
            r.SharpeRatio
            (r.Value.ToString("N0"))
            r.Shares
            r.PctOfPortfolio
            status
    printfn "  %s" divider
    printfn ""
    printfn "  Portfolio: Return=%.2f%%  Risk=%.2f%%  Sharpe=%.2f  Energy=%.4f  Backend=%s"
        (pReturn * 100.0) (pRisk * 100.0) pSharpe energy backendName

printTable ()

// ==============================================================================
// STRUCTURED OUTPUT (JSON / CSV)
// ==============================================================================

let resultMaps : Map<string, string> list =
    sortedResults
    |> List.map (fun r ->
        [ "symbol",              r.Stock.Symbol
          "name",                r.Stock.Name
          "expected_return",     sprintf "%.4f" r.Stock.ExpectedReturn
          "risk",                sprintf "%.4f" r.Stock.Risk
          "price",               sprintf "%.2f" r.Stock.Price
          "shares",              sprintf "%.4f" r.Shares
          "value",               sprintf "%.2f" r.Value
          "pct_of_portfolio",    sprintf "%.2f" r.PctOfPortfolio
          "sharpe_ratio",        sprintf "%.4f" r.SharpeRatio
          "portfolio_return",    sprintf "%.4f" r.PortfolioReturn
          "portfolio_risk",      sprintf "%.4f" r.PortfolioRisk
          "portfolio_sharpe",    sprintf "%.4f" r.PortfolioSharpe
          "best_energy",         sprintf "%.4f" r.BestEnergy
          "backend_name",        r.BackendName
          "solver_elapsed_ms",   sprintf "%.1f" r.SolverElapsedMs
          "budget",              sprintf "%.2f" budget
          "shots",               sprintf "%d" shots
          "risk_aversion",       sprintf "%.2f" riskAversion
          "has_quantum_failure", sprintf "%b" r.HasQuantumFailure ]
        |> Map.ofList)

match outputPath with
| Some path ->
    Reporting.writeJson path resultMaps
    if not quiet then printfn "\nResults written to %s" path
| None -> ()

match csvPath with
| Some path ->
    let header =
        [ "symbol"; "name"; "expected_return"; "risk"; "price"
          "shares"; "value"; "pct_of_portfolio"; "sharpe_ratio"
          "portfolio_return"; "portfolio_risk"; "portfolio_sharpe"
          "best_energy"; "backend_name"; "solver_elapsed_ms"
          "budget"; "shots"; "risk_aversion"; "has_quantum_failure" ]
    let rows =
        resultMaps |> List.map (fun m ->
            header |> List.map (fun h -> m |> Map.tryFind h |> Option.defaultValue ""))
    Reporting.writeCsv path header rows
    if not quiet then printfn "Results written to %s" path
| None -> ()
