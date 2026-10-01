// ==============================================================================
// Regime-Aware Portfolio Optimization
// ==============================================================================
// Adaptive portfolio optimization using Hidden Markov Models (HMM) to detect
// market regimes (Bull/Bear) and apply regime-specific strategies with
// quantum-ready HybridSolver optimization.
//
// Data (default): a two-state Gaussian HMM fitted to SPY daily returns
// 2019-2023 gives the regime on the last trading day of 2023; each asset's
// mean and risk over its last 30 trading days feed the optimizer; the 2023-12-29
// adjusted close is the buy price and 2024 is the hold-out year. The figures are
// in data/market-stats-2019-2023.json; --live recomputes them from Yahoo Finance.
//
// Synthetic mode (--synthetic, or --days/--seed): simulates a Markov chain of
// regimes with the fitted transition probabilities and per-asset Bull/Bear
// return statistics, then detects the regime of the simulated path.
//
// Usage:
//   dotnet fsi RegimeAwarePortfolio.fsx                                       (defaults)
//   dotnet fsi RegimeAwarePortfolio.fsx -- --help                             (show options)
//   dotnet fsi RegimeAwarePortfolio.fsx -- --symbols AAPL,MSFT,GLD,TLT       (select stocks)
//   dotnet fsi RegimeAwarePortfolio.fsx -- --input custom-stocks.csv
//   dotnet fsi RegimeAwarePortfolio.fsx -- --live --from 2015-01-01 --to 2019-12-31
//   dotnet fsi RegimeAwarePortfolio.fsx -- --budget 200000 --days 500        (synthetic)
//   dotnet fsi RegimeAwarePortfolio.fsx -- --quiet --output results.json --csv out.csv
//
// References:
//   [1] Rabiner, "A Tutorial on HMMs", Proc. IEEE 77(2) (1989)
//   [2] Orus et al., "Quantum computing for finance", Rev. Mod. Phys. 91 (2019)
//   [3] https://en.wikipedia.org/wiki/Hidden_Markov_model
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
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Backends.LocalBackend
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
    "RegimeAwarePortfolio.fsx"
    "HMM regime detection + quantum-ready portfolio optimization."
    [
        {
            Cli.OptionSpec.Name = "symbols"
            Description = "Comma-separated symbols (any Yahoo ticker with --live)"
            Default = Some "TQQQ,AAPL,MSFT,JNJ,XLP,GLD,TLT,SH"
        }
        {
            Cli.OptionSpec.Name = "input"
            Description = "CSV: symbol[,name,price] or preset"
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
            Cli.OptionSpec.Name = "synthetic"
            Description = "Simulate returns from the fitted regime model"
            Default = None
        }
        {
            Cli.OptionSpec.Name = "days"
            Description = "Days to simulate (implies --synthetic)"
            Default = Some "252"
        }
        {
            Cli.OptionSpec.Name = "seed"
            Description = "Random seed for the simulation (implies --synthetic)"
            Default = Some "42"
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
let days = Cli.getIntOr "days" 252 args
let seed = Cli.getIntOr "seed" 42 args

let synthetic =
    Cli.hasFlag "synthetic" args
    || (Cli.tryGet "days" args).IsSome
    || (Cli.tryGet "seed" args).IsSome

// ==============================================================================
// DOMAIN TYPES
// ==============================================================================

type MarketRegime =
    | Bull
    | Bear

/// A stock with price data
type StockInfo =
    {
        Symbol: string
        Name: string
        Price: float
    }

/// Per-stock result from regime-aware optimization
type StockResult =
    {
        Stock: StockInfo
        Shares: float
        Value: float
        PctOfPortfolio: float
        SharpeRatio: float
        ExpectedReturn: float
        Risk: float
        DetectedRegime: string
        /// Simulated regime on the last day (synthetic mode only).
        TrueRegime: string option
        RegimeAccurate: bool option
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
// Symbols only: names, prices and return statistics come from the bundled
// statistics or from --live.

let private presetSymbols =
    [ "TQQQ"; "AAPL"; "MSFT"; "JNJ"; "XLP"; "GLD"; "TLT"; "SH" ]

/// A stock to include; Price is set when the CSV supplies it.
type private StockSpec =
    {
        Symbol: string
        Name: string option
        Price: float option
    }

// ==============================================================================
// CSV LOADING
// ==============================================================================

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
                Price = None
            }
        | _ ->
            let symbol = get "symbol"

            if symbol = "" then
                failwithf "Missing symbol in CSV row %d" (i + 1)

            {
                Symbol = symbol.ToUpperInvariant()
                Name =
                    (match get "name" with
                     | "" -> None
                     | n -> Some n)
                Price =
                    match get "price" with
                    | "" -> None
                    | s ->
                        match
                            Double.TryParse(
                                s,
                                Globalization.NumberStyles.Float,
                                Globalization.CultureInfo.InvariantCulture
                            )
                        with
                        | true, v -> Some v
                        | _ -> failwithf "CSV row %d (%s): price '%s' is not a number" (i + 1) symbol s
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
                Price = None
            })

if selectedSpecs.IsEmpty then
    eprintfn "ERROR: No stocks selected. Check --symbols filter or --input CSV."
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
        let symbols = selectedSpecs |> List.map (fun s -> s.Symbol)

        match
            MarketData.computeFromYahooAsync info symbols windowFrom windowTo (Some MarketData.defaultRegimeProxy)
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

let regimeModel =
    match marketStats.RegimeModel with
    | Some m -> m
    | None ->
        eprintfn "ERROR: no regime model: the market proxy %s has no data for the window." MarketData.defaultRegimeProxy
        exit 1

let selectedStocks =
    selectedSpecs
    |> List.choose (fun spec ->
        let figures = MarketData.tryAsset marketStats spec.Symbol

        match spec.Price |> Option.orElse (figures |> Option.map (fun a -> a.BuyPrice)) with
        | Some price when synthetic || figures.IsSome ->
            Some
                {
                    StockInfo.Symbol = spec.Symbol
                    Name =
                        spec.Name
                        |> Option.orElse (figures |> Option.map (fun a -> a.Name))
                        |> Option.defaultValue spec.Symbol
                    Price = price
                }
        | _ ->
            eprintfn "  Skipping %s: no figures for it (not in the bundled data; try --live)" spec.Symbol
            None)

if selectedStocks.IsEmpty then
    eprintfn "ERROR: No stocks with figures."
    exit 1

if not quiet then
    printfn "%s" (MarketData.describeSource marketStats liveUsed)

// ==============================================================================
// SYNTHETIC DATA GENERATION (Markov chain — inherently stateful)
// ==============================================================================

/// Daily (mean, std) on Bull and Bear days: the asset's own figures, else the market proxy's.
let private regimeReturnParams (symbol: string) =
    let proxy =
        (regimeModel.BullMean, regimeModel.BullStd), (regimeModel.BearMean, regimeModel.BearStd)

    MarketData.tryAsset marketStats symbol
    |> Option.bind (fun a -> a.Regime)
    |> Option.map (fun r -> (r.BullDailyMean, r.BullDailyStd), (r.BearDailyMean, r.BearDailyStd))
    |> Option.defaultValue proxy

let private generateMarketData (numDays: int) (rngSeed: int) (stockList: StockInfo list) =
    let rng = Random(rngSeed)
    let p_bull_bear = regimeModel.PBullToBear
    let p_bear_bull = regimeModel.PBearToBull

    let mutable state = Bull
    let marketReturns = Array.zeroCreate numDays
    let regimes = Array.zeroCreate numDays

    let mutable assetReturns =
        stockList
        |> List.map (fun a -> a.Symbol, Array.zeroCreate<float> numDays)
        |> Map.ofList

    for i in 0 .. numDays - 1 do
        if state = Bull && rng.NextDouble() < p_bull_bear then
            state <- Bear
        elif state = Bear && rng.NextDouble() < p_bear_bull then
            state <- Bull

        regimes.[i] <- state

        let (m_mu, m_sigma) =
            if state = Bull then
                (regimeModel.BullMean, regimeModel.BullStd)
            else
                (regimeModel.BearMean, regimeModel.BearStd)

        let u1 = rng.NextDouble()
        let u2 = rng.NextDouble()
        let z = sqrt (-2.0 * log u1) * cos (2.0 * Math.PI * u2)
        marketReturns.[i] <- m_mu + m_sigma * z

        stockList
        |> List.iter (fun asset ->
            let (bullP, bearP) = regimeReturnParams asset.Symbol

            let (mu, sigma) = if state = Bull then bullP else bearP
            let u1_a = rng.NextDouble()
            let u2_a = rng.NextDouble()
            let z_a = sqrt (-2.0 * log u1_a) * cos (2.0 * Math.PI * u2_a)
            assetReturns.[asset.Symbol].[i] <- mu + sigma * z_a)

    (marketReturns, regimes, assetReturns)

// ==============================================================================
// HIDDEN MARKOV MODEL (HMM) — Viterbi Algorithm
// ==============================================================================

module MarketHMM =

    /// Regime on the last day of the Viterbi path under the fitted model (state 0 = Bull).
    let detectRegime (model: MarketData.RegimeModel) (marketReturns: float[]) =
        let path = MarketData.viterbiPath (MarketData.hmmParamsOf model) marketReturns

        match path.[path.Length - 1] with
        | 0 -> Bull
        | _ -> Bear

// ==============================================================================
// REGIME-AWARE OPTIMIZER
// ==============================================================================

module RegimeAwareOptimizer =

    /// Daily mean and standard deviation of the last 30 simulated returns.
    let recentStats (returns: float[]) =
        let recent = returns |> Array.skip (max 0 (returns.Length - 30))
        let mean = Array.average recent
        let sumSq = recent |> Array.sumBy (fun r -> pown (r - mean) 2)
        let vol = sqrt (sumSq / float recent.Length)
        (mean, vol)

    let private toSolverAsset (recent: StockInfo -> float * float) (s: StockInfo) : PortfolioSolver.Asset =
        let (mu, sigma) = recent s

        {
            Symbol = s.Symbol
            ExpectedReturn = mu
            Risk = sigma
            Price = s.Price
        }

    /// `recent` gives each stock's daily (mean, std) over its last 30 trading days.
    let optimize
        (regime: MarketRegime)
        (investBudget: float)
        (qBackend: IQuantumBackend)
        (stockList: StockInfo list)
        (recent: StockInfo -> float * float)
        : System.Threading.Tasks.Task<Result<PortfolioSolver.Allocation list * float * float * float * float * string, string>> =

        let solverAssets = stockList |> List.map (toSolverAsset recent)

        let constraints =
            match regime with
            | Bull ->
                {
                    PortfolioSolver.Constraints.Budget = investBudget
                    PortfolioSolver.Constraints.MinHolding = 0.0
                    PortfolioSolver.Constraints.MaxHolding = investBudget * 0.4
                }
            | Bear ->
                {
                    PortfolioSolver.Constraints.Budget = investBudget
                    PortfolioSolver.Constraints.MinHolding = 0.0
                    PortfolioSolver.Constraints.MaxHolding = investBudget * 0.5
                }

        let method =
            match qBackend with
            | :? LocalBackend -> Some HybridSolver.SolverMethod.Classical
            | _ -> Some HybridSolver.SolverMethod.Quantum

        // Daily covariance S_ij = rho_ij * sigma_i * sigma_j: the window correlations with the
        // same recent daily volatilities the solver's assets carry.
        let dailyCovariance =
            MarketData.covariance marketStats (solverAssets |> List.map (fun a -> a.Symbol, a.Risk))

        task {
            let! solved =
                match dailyCovariance with
                | Some sigma ->
                    HybridSolver.solvePortfolioWithCovarianceAsync
                        solverAssets
                        sigma
                        constraints
                        None
                        None
                        method
                        None
                        System.Threading.CancellationToken.None
                | None ->
                    HybridSolver.solvePortfolioAsync
                        solverAssets
                        constraints
                        None
                        None
                        method
                        System.Threading.CancellationToken.None

            match solved with
            | Ok solution ->
                // Inputs are daily, so the return/risk ratio is annualised by sqrt 252.
                let sharpe =
                    if solution.Result.Risk > 0.0 then
                        solution.Result.ExpectedReturn / solution.Result.Risk * sqrt 252.0
                    else
                        0.0

                let methodStr = $"%A{solution.Method}"

                return
                    Ok(
                        solution.Result.Allocations,
                        solution.Result.TotalValue,
                        solution.Result.ExpectedReturn,
                        solution.Result.Risk,
                        sharpe,
                        methodStr
                    )
            | Error e -> return Error e.Message
        }

// ==============================================================================
// QUANTUM COMPUTATION
// ==============================================================================

if not quiet then
    if synthetic then
        printfn
            "Regime-aware portfolio: %d assets, budget $%s, synthetic %d days, seed %d"
            selectedStocks.Length
            (budget.ToString "N0")
            days
            seed
    else
        printfn "Regime-aware portfolio: %d assets, budget $%s" selectedStocks.Length (budget.ToString "N0")

    printfn
        "  Regime model (%s daily returns): Bull mean %.3f%% sd %.3f%%, Bear mean %.3f%% sd %.3f%%, P(Bull->Bear) %.4f, P(Bear->Bull) %.4f"
        regimeModel.MarketProxy
        (regimeModel.BullMean * 100.0)
        (regimeModel.BullStd * 100.0)
        (regimeModel.BearMean * 100.0)
        (regimeModel.BearStd * 100.0)
        regimeModel.PBullToBear
        regimeModel.PBearToBull

    printfn ""

let backend = LocalBackend() :> IQuantumBackend

// 1. Market returns: simulated from the fitted model, or the real regime from the bundled/live fit
let (detectedRegime, trueRegime, recent) =
    if synthetic then
        if not quiet then
            printfn "Simulating %d days from the fitted regime model (seed %d)..." days seed

        let (marketData, trueRegimes, assetHistory) =
            generateMarketData days seed selectedStocks

        // 2. Detect regime via HMM Viterbi
        if not quiet then
            printfn "Detecting market regime (HMM Viterbi)..."

        let recentOf (s: StockInfo) =
            RegimeAwareOptimizer.recentStats assetHistory.[s.Symbol]

        MarketHMM.detectRegime regimeModel marketData, Some trueRegimes.[days - 1], recentOf
    else
        let lastDay =
            MarketData.tryAsset marketStats regimeModel.MarketProxy
            |> Option.map (fun a -> a.BuyDate)
            |> Option.defaultValue marketStats.WindowTo

        if not quiet then
            printfn
                "HMM Viterbi over %s..%s: %d Bull days, %d Bear days; regime on %s: %s"
                marketStats.WindowFrom
                marketStats.WindowTo
                regimeModel.BullDays
                regimeModel.BearDays
                lastDay
                regimeModel.RegimeAtWindowEnd

        let recentOf (s: StockInfo) =
            match MarketData.tryAsset marketStats s.Symbol with
            | Some a -> (a.Recent30DailyMean, a.Recent30DailyStd)
            | None -> failwithf "no figures for %s" s.Symbol

        (if regimeModel.RegimeAtWindowEnd = "Bear" then
             Bear
         else
             Bull),
        None,
        recentOf

let regimeAccurate = trueRegime |> Option.map (fun t -> t = detectedRegime)

let strategy =
    match detectedRegime with
    | Bull -> "Maximize Growth"
    | Bear -> "Capital Preservation"

if not quiet then
    match trueRegime, regimeAccurate with
    | Some t, Some ok ->
        printfn "  Detected: %A  |  Simulated: %A  |  %s" detectedRegime t (if ok then "Accurate" else "Mismatch")
    | _ -> printfn "  Detected: %A" detectedRegime

    printfn "  Strategy: %s" strategy
    printfn ""

// 3. Optimize portfolio
let sortedResults =
    // Script top level: wait for the optimizer here.
    let optimized =
        RegimeAwareOptimizer.optimize detectedRegime budget backend selectedStocks recent
        |> Async.AwaitTask
        |> Async.RunSynchronously

    match optimized with
    | Ok(allocations, totalValue, pReturn, pRisk, pSharpe, methodStr) ->
        selectedStocks
        |> List.map (fun stock ->
            let alloc = allocations |> List.tryFind (fun a -> a.Asset.Symbol = stock.Symbol)
            let shares = alloc |> Option.map (fun a -> a.Shares) |> Option.defaultValue 0.0
            let value = alloc |> Option.map (fun a -> a.Value) |> Option.defaultValue 0.0
            let pct = if totalValue > 0.0 then value / totalValue * 100.0 else 0.0

            let (assetReturn, assetRisk) = recent stock
            // Sharpe uses EXCESS return over the risk-free rate; both are daily here.
            let riskFreeRate = 0.02 / 252.0 // 2% a year; ~short-term T-bill proxy

            let sharpe =
                if assetRisk > 0.0 then
                    (assetReturn - riskFreeRate) / assetRisk * sqrt 252.0
                else
                    0.0

            {
                Stock = stock
                Shares = shares
                Value = value
                PctOfPortfolio = pct
                SharpeRatio = sharpe
                ExpectedReturn = assetReturn
                Risk = assetRisk
                DetectedRegime = $"%A{detectedRegime}"
                TrueRegime = trueRegime |> Option.map (sprintf "%A")
                RegimeAccurate = regimeAccurate
                Strategy = strategy
                PortfolioReturn = pReturn
                PortfolioRisk = pRisk
                PortfolioSharpe = pSharpe
                SolverMethod = methodStr
                HasOptimizationFailure = false
            })
        |> List.sortByDescending (fun r -> r.Value)

    | Error err ->
        if not quiet then
            eprintfn "Optimization failed: %s" err

        selectedStocks
        |> List.map (fun stock ->
            {
                Stock = stock
                Shares = 0.0
                Value = 0.0
                PctOfPortfolio = 0.0
                SharpeRatio = 0.0
                ExpectedReturn = 0.0
                Risk = 0.0
                DetectedRegime = $"%A{detectedRegime}"
                TrueRegime = trueRegime |> Option.map (sprintf "%A")
                RegimeAccurate = regimeAccurate
                Strategy = strategy
                PortfolioReturn = 0.0
                PortfolioRisk = 0.0
                PortfolioSharpe = 0.0
                SolverMethod = "Error"
                HasOptimizationFailure = true
            })

let chosenWeights = sortedResults |> List.map (fun r -> r.Stock.Symbol, r.Value)
let equalWeights = selectedStocks |> List.map (fun s -> s.Symbol, 1.0)

let private fmtOption (x: float option) =
    x
    |> Option.map (fun v -> v.ToString("F4", Globalization.CultureInfo.InvariantCulture))
    |> Option.defaultValue ""

let private holdoutFor (weights: (string * float) list) =
    if synthetic then
        None
    else
        MarketData.holdoutReturn marketStats weights

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

    let divider = String('-', 102)
    printfn ""

    printfn
        "  Regime-Aware Portfolio (budget $%s, regime=%A, strategy=%s)"
        (budget.ToString "N0")
        detectedRegime
        strategy

    printfn "  %s" divider

    printfn
        "  %-6s %-18s %8s %8s %8s %10s %7s %7s %8s"
        "Symbol"
        "Name"
        "Ret/day"
        "Risk/day"
        "Sharpe"
        "Value"
        "Shares"
        "Pct"
        "Status"

    printfn "  %s" divider

    for r in sortedResults do
        let status = if r.HasOptimizationFailure then "FAIL" else "OK"

        printfn
            "  %-6s %-18s %7.3f%% %7.3f%% %8.2f $%9s %7.2f %5.1f%% %8s"
            r.Stock.Symbol
            (if r.Stock.Name.Length > 18 then
                 r.Stock.Name.[..17]
             else
                 r.Stock.Name)
            (r.ExpectedReturn * 100.0)
            (r.Risk * 100.0)
            r.SharpeRatio
            (r.Value.ToString "N0")
            r.Shares
            r.PctOfPortfolio
            status

    printfn "  %s" divider
    printfn ""

    printfn
        "  Portfolio: Return=%.4f%%/day  Risk=%.4f%%/day  Sharpe=%.2f (annualised)  Method=%s"
        (pReturn * 100.0)
        (pRisk * 100.0)
        pSharpe
        (first |> Option.map (fun r -> r.SolverMethod) |> Option.defaultValue "N/A")

    printfn "  Ret/day and Risk/day: mean and standard deviation of the last 30 daily returns."
    printfn "  Portfolio Risk/day: sqrt(w' S w) with S = window correlations x these daily deviations."

    let failed = sortedResults |> List.exists (fun r -> r.HasOptimizationFailure)

    if synthetic then
        printfn "  Synthetic mode: returns are simulated, so there is no hold-out comparison."
    elif not failed then
        MarketData.printComparison marketStats chosenWeights (selectedStocks |> List.map (fun s -> s.Symbol)) None

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
            "price", $"%.2f{r.Stock.Price}"
            "expected_return", $"%.6f{r.ExpectedReturn}"
            "risk", $"%.6f{r.Risk}"
            "sharpe_ratio", $"%.4f{r.SharpeRatio}"
            "shares", $"%.4f{r.Shares}"
            "value", $"%.2f{r.Value}"
            "pct_of_portfolio", $"%.2f{r.PctOfPortfolio}"
            "detected_regime", r.DetectedRegime
            "true_regime", r.TrueRegime |> Option.defaultValue ""
            "regime_accurate", r.RegimeAccurate |> Option.map (sprintf "%b") |> Option.defaultValue ""
            "strategy", r.Strategy
            "portfolio_return", $"%.6f{r.PortfolioReturn}"
            "portfolio_risk", $"%.6f{r.PortfolioRisk}"
            "portfolio_sharpe", $"%.4f{r.PortfolioSharpe}"
            "solver_method", r.SolverMethod
            "budget", $"%.2f{budget}"
            "days", (if synthetic then $"%d{days}" else "")
            "seed", (if synthetic then $"%d{seed}" else "")
            "has_optimization_failure", $"%b{r.HasOptimizationFailure}"
            "data_mode", (if synthetic then "synthetic" else "real")
            "holdout_return",
            (if synthetic then
                 ""
             else
                 fmtOption (
                     MarketData.tryAsset marketStats r.Stock.Symbol
                     |> Option.bind (fun a -> a.HoldoutReturn)
                 ))
            "portfolio_holdout_return", fmtOption (holdoutFor chosenWeights)
            "equal_weight_holdout_return", fmtOption (holdoutFor equalWeights)
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
            "price"
            "expected_return"
            "risk"
            "sharpe_ratio"
            "shares"
            "value"
            "pct_of_portfolio"
            "detected_regime"
            "true_regime"
            "regime_accurate"
            "strategy"
            "portfolio_return"
            "portfolio_risk"
            "portfolio_sharpe"
            "solver_method"
            "budget"
            "days"
            "seed"
            "has_optimization_failure"
            "data_mode"
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
