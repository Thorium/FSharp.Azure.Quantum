// ==============================================================================
// Regime-Aware Portfolio Optimization
// ==============================================================================
// Adaptive portfolio optimization using Hidden Markov Models (HMM) to detect
// market regimes (Bull/Bear) and apply regime-specific strategies with
// quantum-ready HybridSolver optimization.
//
// Usage:
//   dotnet fsi RegimeAwarePortfolio.fsx                                       (defaults)
//   dotnet fsi RegimeAwarePortfolio.fsx -- --help                             (show options)
//   dotnet fsi RegimeAwarePortfolio.fsx -- --symbols AAPL,MSFT,GLD,TLT       (select stocks)
//   dotnet fsi RegimeAwarePortfolio.fsx -- --input custom-stocks.csv
//   dotnet fsi RegimeAwarePortfolio.fsx -- --budget 200000 --days 500
//   dotnet fsi RegimeAwarePortfolio.fsx -- --quiet --output results.json --csv out.csv
//
// References:
//   [1] Rabiner, "A Tutorial on HMMs", Proc. IEEE 77(2) (1989)
//   [2] Orus et al., "Quantum computing for finance", Rev. Mod. Phys. 91 (2019)
//   [3] https://en.wikipedia.org/wiki/Hidden_Markov_model
// ==============================================================================

//#r "nuget: FSharp.Azure.Quantum"
#r "../../src/FSharp.Azure.Quantum/bin/Debug/net10.0/FSharp.Azure.Quantum.dll"

#load "../_common/Cli.fs"
#load "../_common/Data.fs"
#load "../_common/Reporting.fs"

open System
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Backends.LocalBackend
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.Classical
open FSharp.Azure.Quantum.Examples.Common

// ==============================================================================
// CLI ARGUMENT PARSING
// ==============================================================================

let argv = fsi.CommandLineArgs |> Array.skip 1
let args = Cli.parse argv

Cli.exitIfHelp
    "RegimeAwarePortfolio.fsx"
    "HMM regime detection + quantum-ready portfolio optimization."
    [ { Cli.OptionSpec.Name = "symbols"; Description = "Comma-separated stock symbols to include"; Default = None }
      { Cli.OptionSpec.Name = "input";   Description = "CSV file with custom stock definitions";   Default = None }
      { Cli.OptionSpec.Name = "budget";  Description = "Investment budget in dollars";              Default = Some "100000" }
      { Cli.OptionSpec.Name = "days";    Description = "Days of market data to generate";           Default = Some "252" }
      { Cli.OptionSpec.Name = "seed";    Description = "Random seed for data generation";           Default = Some "42" }
      { Cli.OptionSpec.Name = "output";  Description = "Write results to JSON file";                Default = None }
      { Cli.OptionSpec.Name = "csv";     Description = "Write results to CSV file";                 Default = None }
      { Cli.OptionSpec.Name = "quiet";   Description = "Suppress informational output";             Default = None } ]
    args

let quiet = Cli.hasFlag "quiet" args
let outputPath = Cli.tryGet "output" args
let csvPath = Cli.tryGet "csv" args
let budget = Cli.getFloatOr "budget" 100000.0 args
let days = Cli.getIntOr "days" 252 args
let seed = Cli.getIntOr "seed" 42 args

// ==============================================================================
// DOMAIN TYPES
// ==============================================================================

type MarketRegime = Bull | Bear

/// A stock with price data
type StockInfo = {
    Symbol: string
    Name: string
    Price: float
}

/// Per-stock result from regime-aware optimization
type StockResult = {
    Stock: StockInfo
    Shares: float
    Value: float
    PctOfPortfolio: float
    SharpeRatio: float
    ExpectedReturn: float
    Risk: float
    DetectedRegime: string
    TrueRegime: string
    RegimeAccurate: bool
    Strategy: string
    PortfolioReturn: float
    PortfolioRisk: float
    PortfolioSharpe: float
    SolverMethod: string
    HasOptimizationFailure: bool
}

// ==============================================================================
// BUILT-IN STOCK PRESETS
// ==============================================================================

let private presetTqqq = { Symbol = "TQQQ"; Name = "Tech Aggressive";    Price = 50.0 }
let private presetAapl = { Symbol = "AAPL"; Name = "Apple";              Price = 175.0 }
let private presetMsft = { Symbol = "MSFT"; Name = "Microsoft";          Price = 380.0 }
let private presetJnj  = { Symbol = "JNJ";  Name = "Johnson&Johnson";    Price = 160.0 }
let private presetXlp  = { Symbol = "XLP";  Name = "Consumer Staples";   Price = 75.0 }
let private presetGld  = { Symbol = "GLD";  Name = "Gold";               Price = 190.0 }
let private presetTlt  = { Symbol = "TLT";  Name = "Treasury Bonds";     Price = 95.0 }
let private presetSh   = { Symbol = "SH";   Name = "Short S&P500";       Price = 15.0 }

let private builtInStocks =
    [ presetTqqq; presetAapl; presetMsft; presetJnj; presetXlp; presetGld; presetTlt; presetSh ]
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
            { Symbol = let s = get "symbol" in if s = "" then failwithf "Missing symbol in CSV row %d" (i + 1) else s.ToUpperInvariant()
              Name   = let n = get "name" in if n = "" then get "symbol" else n
              Price  = get "price" |> fun s -> match Double.TryParse s with true, v -> v | _ -> 100.0 })

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
// SYNTHETIC DATA GENERATION (Markov chain — inherently stateful)
// ==============================================================================

/// Per-asset return parameters: (bullMu, bullSigma), (bearMu, bearSigma)
let private assetReturnParams =
    [ ("TQQQ", ((0.0015, 0.02), (-0.003, 0.05)))
      ("AAPL", ((0.001, 0.012), (-0.001, 0.025)))
      ("MSFT", ((0.0009, 0.011), (-0.001, 0.022)))
      ("JNJ",  ((0.0003, 0.008), (-0.0005, 0.01)))
      ("XLP",  ((0.0002, 0.007), (-0.0002, 0.008)))
      ("GLD",  ((0.0002, 0.009), (0.0006, 0.012)))
      ("TLT",  ((0.0001, 0.006), (0.0004, 0.008)))
      ("SH",   ((-0.0005, 0.012), (0.0015, 0.025))) ]
    |> Map.ofList

/// Default params for stocks not in the built-in param table
let private defaultReturnParams = ((0.0005, 0.012), (-0.0005, 0.020))

let private generateMarketData (numDays: int) (rngSeed: int) (stockList: StockInfo list) =
    let rng = Random(rngSeed)
    let p_bull_bear = 0.05
    let p_bear_bull = 0.10

    let mutable state = Bull
    let marketReturns = Array.zeroCreate numDays
    let regimes = Array.zeroCreate numDays

    let mutable assetReturns =
        stockList |> List.map (fun a -> a.Symbol, Array.zeroCreate<float> numDays) |> Map.ofList

    for i in 0 .. numDays - 1 do
        if state = Bull && rng.NextDouble() < p_bull_bear then state <- Bear
        elif state = Bear && rng.NextDouble() < p_bear_bull then state <- Bull

        regimes.[i] <- state

        let (m_mu, m_sigma) = if state = Bull then (0.0005, 0.01) else (-0.001, 0.03)
        let u1 = rng.NextDouble()
        let u2 = rng.NextDouble()
        let z = sqrt(-2.0 * log u1) * cos(2.0 * Math.PI * u2)
        marketReturns.[i] <- m_mu + m_sigma * z

        stockList |> List.iter (fun asset ->
            let (bullP, bearP) =
                assetReturnParams
                |> Map.tryFind asset.Symbol
                |> Option.defaultValue defaultReturnParams
            let (mu, sigma) = if state = Bull then bullP else bearP
            let u1_a = rng.NextDouble()
            let u2_a = rng.NextDouble()
            let z_a = sqrt(-2.0 * log u1_a) * cos(2.0 * Math.PI * u2_a)
            assetReturns.[asset.Symbol].[i] <- mu + sigma * z_a)

    (marketReturns, regimes, assetReturns)

// ==============================================================================
// HIDDEN MARKOV MODEL (HMM) — Viterbi Algorithm
// ==============================================================================

module MarketHMM =

    let private gaussianPdf x mu sigma =
        let coeff = 1.0 / (sigma * sqrt(2.0 * Math.PI))
        let exponent = -0.5 * ((x - mu) / sigma) ** 2.0
        coeff * exp exponent

    let detectRegime (marketReturns: float[]) =
        let bullMu, bullSigma = 0.0005, 0.01
        let bearMu, bearSigma = -0.002, 0.03
        let trans = array2D [[0.95; 0.05]; [0.10; 0.90]]
        let startP = [| 0.7; 0.3 |]

        let T = marketReturns.Length
        let nStates = 2

        let V = Array2D.create T nStates Double.NegativeInfinity
        let path = Array2D.create T nStates 0

        let x0 = marketReturns.[0]
        V.[0,0] <- log(startP.[0]) + log(gaussianPdf x0 bullMu bullSigma)
        V.[0,1] <- log(startP.[1]) + log(gaussianPdf x0 bearMu bearSigma)

        for t in 1 .. T - 1 do
            let xt = marketReturns.[t]
            let emitBull = log(gaussianPdf xt bullMu bullSigma)
            let emitBear = log(gaussianPdf xt bearMu bearSigma)

            let fromBull0 = V.[t-1, 0] + log(trans.[0,0])
            let fromBear0 = V.[t-1, 1] + log(trans.[1,0])
            if fromBull0 > fromBear0 then
                V.[t,0] <- fromBull0 + emitBull
                path.[t,0] <- 0
            else
                V.[t,0] <- fromBear0 + emitBull
                path.[t,0] <- 1

            let fromBull1 = V.[t-1, 0] + log(trans.[0,1])
            let fromBear1 = V.[t-1, 1] + log(trans.[1,1])
            if fromBull1 > fromBear1 then
                V.[t,1] <- fromBull1 + emitBear
                path.[t,1] <- 0
            else
                V.[t,1] <- fromBear1 + emitBear
                path.[t,1] <- 1

        let lastState = if V.[T-1,0] > V.[T-1,1] then 0 else 1
        match lastState with
        | 0 -> Bull
        | _ -> Bear

// ==============================================================================
// REGIME-AWARE OPTIMIZER
// ==============================================================================

module RegimeAwareOptimizer =

    let private calculateStats (returns: float[]) =
        let mean = Array.average returns
        let sumSq = returns |> Array.sumBy (fun r -> pown (r - mean) 2)
        let vol = sqrt (sumSq / float returns.Length)
        (mean, vol)

    let private toSolverAsset (history: Map<string, float[]>) (s: StockInfo) : PortfolioSolver.Asset =
        let recentReturns =
            match history.TryFind s.Symbol with
            | Some r -> r |> Array.skip (max 0 (r.Length - 30))
            | None -> [| 0.0 |]
        let (mu, sigma) = calculateStats recentReturns
        { Symbol = s.Symbol; ExpectedReturn = mu; Risk = sigma; Price = s.Price }

    let optimize
        (regime: MarketRegime)
        (investBudget: float)
        (qBackend: IQuantumBackend)
        (stockList: StockInfo list)
        (assetHistory: Map<string, float[]>)
        : Result<PortfolioSolver.Allocation list * float * float * float * float * string, string> =

        let solverAssets = stockList |> List.map (toSolverAsset assetHistory)

        let constraints =
            match regime with
            | Bull ->
                { PortfolioSolver.Constraints.Budget = investBudget
                  PortfolioSolver.Constraints.MinHolding = 0.0
                  PortfolioSolver.Constraints.MaxHolding = investBudget * 0.4 }
            | Bear ->
                { PortfolioSolver.Constraints.Budget = investBudget
                  PortfolioSolver.Constraints.MinHolding = 0.0
                  PortfolioSolver.Constraints.MaxHolding = investBudget * 0.5 }

        let method =
            match qBackend with
            | :? LocalBackend -> Some HybridSolver.SolverMethod.Classical
            | _ -> Some HybridSolver.SolverMethod.Quantum

        match HybridSolver.solvePortfolio solverAssets constraints None None method with
        | Ok solution ->
            let sharpe =
                if solution.Result.Risk > 0.0 then solution.Result.ExpectedReturn / solution.Result.Risk
                else 0.0
            let methodStr = sprintf "%A" solution.Method
            Ok (solution.Result.Allocations, solution.Result.TotalValue, solution.Result.ExpectedReturn, solution.Result.Risk, sharpe, methodStr)
        | Error e -> Error e.Message

// ==============================================================================
// QUANTUM COMPUTATION
// ==============================================================================

if not quiet then
    printfn "Regime-aware portfolio: %d assets, budget $%s, %d days, seed %d"
        selectedStocks.Length (budget.ToString("N0")) days seed
    printfn ""

let backend = LocalBackend() :> IQuantumBackend

// 1. Generate synthetic market data
if not quiet then printfn "Generating %d days of market data (seed %d)..." days seed
let (marketData, trueRegimes, assetHistory) = generateMarketData days seed selectedStocks

// 2. Detect regime via HMM Viterbi
if not quiet then printfn "Detecting market regime (HMM Viterbi)..."
let detectedRegime = MarketHMM.detectRegime marketData
let trueRegime = trueRegimes.[days - 1]
let regimeAccurate = detectedRegime = trueRegime
let strategy = match detectedRegime with Bull -> "Maximize Growth" | Bear -> "Capital Preservation"

if not quiet then
    printfn "  Detected: %A  |  True: %A  |  %s"
        detectedRegime trueRegime (if regimeAccurate then "Accurate" else "Mismatch")
    printfn "  Strategy: %s" strategy
    printfn ""

// 3. Optimize portfolio
let sortedResults =
    match RegimeAwareOptimizer.optimize detectedRegime budget backend selectedStocks assetHistory with
    | Ok (allocations, totalValue, pReturn, pRisk, pSharpe, methodStr) ->
        selectedStocks |> List.map (fun stock ->
            let alloc = allocations |> List.tryFind (fun a -> a.Asset.Symbol = stock.Symbol)
            let shares = alloc |> Option.map (fun a -> a.Shares) |> Option.defaultValue 0.0
            let value = alloc |> Option.map (fun a -> a.Value) |> Option.defaultValue 0.0
            let pct = if totalValue > 0.0 then value / totalValue * 100.0 else 0.0
            let assetReturn = alloc |> Option.map (fun a -> a.Asset.ExpectedReturn) |> Option.defaultValue 0.0
            let assetRisk = alloc |> Option.map (fun a -> a.Asset.Risk) |> Option.defaultValue 0.0
            let sharpe = if assetRisk > 0.0 then assetReturn / assetRisk else 0.0
            { Stock = stock
              Shares = shares
              Value = value
              PctOfPortfolio = pct
              SharpeRatio = sharpe
              ExpectedReturn = assetReturn
              Risk = assetRisk
              DetectedRegime = sprintf "%A" detectedRegime
              TrueRegime = sprintf "%A" trueRegime
              RegimeAccurate = regimeAccurate
              Strategy = strategy
              PortfolioReturn = pReturn
              PortfolioRisk = pRisk
              PortfolioSharpe = pSharpe
              SolverMethod = methodStr
              HasOptimizationFailure = false })
        |> List.sortByDescending (fun r -> r.Value)

    | Error err ->
        if not quiet then eprintfn "Optimization failed: %s" err
        selectedStocks |> List.map (fun stock ->
            { Stock = stock; Shares = 0.0; Value = 0.0; PctOfPortfolio = 0.0
              SharpeRatio = 0.0; ExpectedReturn = 0.0; Risk = 0.0
              DetectedRegime = sprintf "%A" detectedRegime
              TrueRegime = sprintf "%A" trueRegime
              RegimeAccurate = regimeAccurate
              Strategy = strategy
              PortfolioReturn = 0.0; PortfolioRisk = 0.0; PortfolioSharpe = 0.0
              SolverMethod = "Error"; HasOptimizationFailure = true })

// ==============================================================================
// COMPARISON TABLE (unconditional)
// ==============================================================================

let printTable () =
    let first = sortedResults |> List.tryHead
    let pReturn = first |> Option.map (fun r -> r.PortfolioReturn) |> Option.defaultValue 0.0
    let pRisk = first |> Option.map (fun r -> r.PortfolioRisk) |> Option.defaultValue 0.0
    let pSharpe = first |> Option.map (fun r -> r.PortfolioSharpe) |> Option.defaultValue 0.0

    let divider = String('-', 102)
    printfn ""
    printfn "  Regime-Aware Portfolio (budget $%s, regime=%A, strategy=%s)"
        (budget.ToString("N0")) detectedRegime strategy
    printfn "  %s" divider
    printfn "  %-6s %-18s %8s %8s %8s %10s %7s %7s %8s"
        "Symbol" "Name" "Return" "Risk" "Sharpe" "Value" "Shares" "Pct" "Status"
    printfn "  %s" divider
    for r in sortedResults do
        let status = if r.HasOptimizationFailure then "FAIL" else "OK"
        printfn "  %-6s %-18s %7.3f%% %7.3f%% %8.2f $%9s %7.2f %5.1f%% %8s"
            r.Stock.Symbol
            (if r.Stock.Name.Length > 18 then r.Stock.Name.[..17] else r.Stock.Name)
            (r.ExpectedReturn * 100.0)
            (r.Risk * 100.0)
            r.SharpeRatio
            (r.Value.ToString("N0"))
            r.Shares
            r.PctOfPortfolio
            status
    printfn "  %s" divider
    printfn ""
    printfn "  Portfolio: Return=%.4f%%  Risk=%.4f%%  Sharpe=%.2f  Method=%s"
        (pReturn * 100.0) (pRisk * 100.0) pSharpe
        (first |> Option.map (fun r -> r.SolverMethod) |> Option.defaultValue "N/A")

printTable ()

// ==============================================================================
// STRUCTURED OUTPUT (JSON / CSV)
// ==============================================================================

let resultMaps : Map<string, string> list =
    sortedResults
    |> List.map (fun r ->
        [ "symbol",                      r.Stock.Symbol
          "name",                        r.Stock.Name
          "price",                       sprintf "%.2f" r.Stock.Price
          "expected_return",             sprintf "%.6f" r.ExpectedReturn
          "risk",                        sprintf "%.6f" r.Risk
          "sharpe_ratio",                sprintf "%.4f" r.SharpeRatio
          "shares",                      sprintf "%.4f" r.Shares
          "value",                       sprintf "%.2f" r.Value
          "pct_of_portfolio",            sprintf "%.2f" r.PctOfPortfolio
          "detected_regime",             r.DetectedRegime
          "true_regime",                 r.TrueRegime
          "regime_accurate",             sprintf "%b" r.RegimeAccurate
          "strategy",                    r.Strategy
          "portfolio_return",            sprintf "%.6f" r.PortfolioReturn
          "portfolio_risk",              sprintf "%.6f" r.PortfolioRisk
          "portfolio_sharpe",            sprintf "%.4f" r.PortfolioSharpe
          "solver_method",               r.SolverMethod
          "budget",                      sprintf "%.2f" budget
          "days",                        sprintf "%d" days
          "seed",                        sprintf "%d" seed
          "has_optimization_failure",    sprintf "%b" r.HasOptimizationFailure ]
        |> Map.ofList)

match outputPath with
| Some path ->
    Reporting.writeJson path resultMaps
    if not quiet then printfn "\nResults written to %s" path
| None -> ()

match csvPath with
| Some path ->
    let header =
        [ "symbol"; "name"; "price"; "expected_return"; "risk"; "sharpe_ratio"
          "shares"; "value"; "pct_of_portfolio"
          "detected_regime"; "true_regime"; "regime_accurate"; "strategy"
          "portfolio_return"; "portfolio_risk"; "portfolio_sharpe"
          "solver_method"; "budget"; "days"; "seed"; "has_optimization_failure" ]
    let rows =
        resultMaps |> List.map (fun m ->
            header |> List.map (fun h -> m |> Map.tryFind h |> Option.defaultValue ""))
    Reporting.writeCsv path header rows
    if not quiet then printfn "Results written to %s" path
| None -> ()
