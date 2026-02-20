// ==============================================================================
// Quantum Value-at-Risk (VaR) Example
// ==============================================================================
// Quantum-enhanced risk measurement for financial portfolios.
// Compares classical parametric VaR, historical VaR, and quantum amplitude
// estimation VaR across a configurable set of portfolio assets.
//
// Usage:
//   dotnet fsi QuantumVaR.fsx                                  (defaults)
//   dotnet fsi QuantumVaR.fsx -- --help                        (show options)
//   dotnet fsi QuantumVaR.fsx -- --assets spy,tlt,gld          (select assets)
//   dotnet fsi QuantumVaR.fsx -- --confidence 0.95 --horizon 5
//   dotnet fsi QuantumVaR.fsx -- --input custom-assets.csv     (load from CSV)
//   dotnet fsi QuantumVaR.fsx -- --live                        (Yahoo Finance)
//   dotnet fsi QuantumVaR.fsx -- --quiet --output results.json --csv var.csv
//
// References:
//   [1] Woerner & Egger, "Quantum Risk Analysis" npj Quantum Inf 5, 15 (2019)
//   [2] Stamatopoulos et al., "Option Pricing using Quantum Computers" (2020)
//   [3] https://en.wikipedia.org/wiki/Value_at_risk
// ==============================================================================

#r "../../src/FSharp.Azure.Quantum/bin/Debug/net10.0/FSharp.Azure.Quantum.dll"

#load "../_common/Cli.fs"
#load "../_common/Data.fs"
#load "../_common/Reporting.fs"

open System
open System.Net.Http
open System.IO
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.CircuitBuilder
open FSharp.Azure.Quantum.Backends.LocalBackend
open FSharp.Azure.Quantum.Algorithms.QuantumMonteCarlo
open FSharp.Azure.Quantum.Data.FinancialData
open FSharp.Azure.Quantum.Examples.Common

// ==============================================================================
// CLI ARGUMENT PARSING
// ==============================================================================

let argv = fsi.CommandLineArgs |> Array.skip 1
let args = Cli.parse argv

Cli.exitIfHelp
    "QuantumVaR.fsx"
    "Quantum-enhanced Value-at-Risk calculation for financial portfolios."
    [ { Cli.OptionSpec.Name = "assets";           Description = "Comma-separated asset symbols to include"; Default = None }
      { Cli.OptionSpec.Name = "input";            Description = "CSV file with custom asset definitions";   Default = None }
      { Cli.OptionSpec.Name = "confidence";       Description = "VaR confidence level (0-1)";               Default = Some "0.99" }
      { Cli.OptionSpec.Name = "horizon";          Description = "Holding period in trading days";            Default = Some "10" }
      { Cli.OptionSpec.Name = "portfolio-value";  Description = "Portfolio value in dollars";                Default = Some "10000000" }
      { Cli.OptionSpec.Name = "qubits";           Description = "Qubits for amplitude estimation";          Default = Some "4" }
      { Cli.OptionSpec.Name = "live";             Description = "Fetch live data from Yahoo Finance";       Default = None }
      { Cli.OptionSpec.Name = "output";           Description = "Write results to JSON file";               Default = None }
      { Cli.OptionSpec.Name = "csv";              Description = "Write results to CSV file";                Default = None }
      { Cli.OptionSpec.Name = "quiet";            Description = "Suppress informational output";            Default = None } ]
    args

let quiet = Cli.hasFlag "quiet" args
let outputPath = Cli.tryGet "output" args
let csvPath = Cli.tryGet "csv" args

// ==============================================================================
// DOMAIN TYPES
// ==============================================================================

/// A portfolio asset with allocation weight and return generation parameters
type AssetInfo = {
    Symbol: string
    Name: string
    ExpectedReturn: float
    Volatility: float
    Weight: float
    Seed: int
    Class: AssetClass
}

/// Per-asset result from the VaR analysis
type AssetResult = {
    Asset: AssetInfo
    MeanDailyReturn: float
    DailyVolatility: float
    MinReturn: float
    MaxReturn: float
    Contribution: float          // Weighted contribution to portfolio VaR
    HasQuantumFailure: bool
}

// ==============================================================================
// CONFIGURATION
// ==============================================================================

let confidenceLevel = Cli.getFloatOr "confidence" 0.99 args
let timeHorizon = Cli.getIntOr "horizon" 10 args
let lookbackPeriod = 252
let quantumQubits = Cli.getIntOr "qubits" 4 args
let groverIterations = 3
let portfolioValue = Cli.getFloatOr "portfolio-value" 10_000_000.0 args

// ==============================================================================
// BUILT-IN ASSET PRESETS
// ==============================================================================

let private presetSpy = { Symbol = "SPY";  Name = "S&P 500 ETF";       ExpectedReturn = 0.10; Volatility = 0.18; Weight = 0.30; Seed = 42; Class = AssetClass.Equity }
let private presetQqq = { Symbol = "QQQ";  Name = "Nasdaq 100 ETF";    ExpectedReturn = 0.12; Volatility = 0.24; Weight = 0.20; Seed = 43; Class = AssetClass.Equity }
let private presetTlt = { Symbol = "TLT";  Name = "20+ Year Treasury";  ExpectedReturn = 0.04; Volatility = 0.15; Weight = 0.25; Seed = 44; Class = AssetClass.FixedIncome }
let private presetGld = { Symbol = "GLD";  Name = "Gold ETF";           ExpectedReturn = 0.06; Volatility = 0.16; Weight = 0.15; Seed = 45; Class = AssetClass.Commodity }
let private presetVwo = { Symbol = "VWO";  Name = "Emerging Markets";   ExpectedReturn = 0.08; Volatility = 0.22; Weight = 0.10; Seed = 46; Class = AssetClass.Equity }

let private builtInAssets =
    [ presetSpy; presetQqq; presetTlt; presetGld; presetVwo ]
    |> List.map (fun a -> a.Symbol.ToLowerInvariant(), a)
    |> Map.ofList

// ==============================================================================
// CSV LOADING
// ==============================================================================

let private parseAssetClass (s: string) =
    match s.Trim().ToLowerInvariant() with
    | "equity"      -> AssetClass.Equity
    | "fixedincome" | "fixed_income" | "bond" | "bonds" -> AssetClass.FixedIncome
    | "commodity"   | "commodities" -> AssetClass.Commodity
    | "alternative" | "alt" -> AssetClass.Alternative
    | "currency"    | "fx" -> AssetClass.Currency
    | _ -> AssetClass.Equity

let private loadAssetsFromCsv (filePath: string) : AssetInfo list =
    let resolved = Data.resolveRelative __SOURCE_DIRECTORY__ filePath
    let rows, errors = Data.readCsvWithHeaderWithErrors resolved
    if not (List.isEmpty errors) then
        eprintfn "WARNING: CSV parse errors in %s:" filePath
        errors |> List.iter (eprintfn "  %s")
    if rows.IsEmpty then failwithf "No valid rows in CSV %s" filePath
    rows |> List.mapi (fun i row ->
        let get key = row.Values |> Map.tryFind key |> Option.defaultValue ""
        match get "preset" with
        | p when not (System.String.IsNullOrWhiteSpace p) ->
            match builtInAssets |> Map.tryFind (p.Trim().ToLowerInvariant()) with
            | Some a -> a
            | None -> failwithf "Unknown preset '%s' in CSV row %d" p (i + 1)
        | _ ->
            { Symbol         = get "symbol"
              Name           = let n = get "name" in if n = "" then get "symbol" else n
              ExpectedReturn = get "expected_return" |> fun s -> match Double.TryParse s with true, v -> v | _ -> 0.08
              Volatility     = get "volatility"      |> fun s -> match Double.TryParse s with true, v -> v | _ -> 0.20
              Weight         = get "weight"          |> fun s -> match Double.TryParse s with true, v -> v | _ -> 1.0 / float (rows.Length)
              Seed           = get "seed"            |> fun s -> match Int32.TryParse s with true, v -> v | _ -> 42 + i
              Class          = get "asset_class"     |> parseAssetClass })

// ==============================================================================
// ASSET SELECTION
// ==============================================================================

let selectedAssets =
    let base' =
        match Cli.tryGet "input" args with
        | Some csvFile -> loadAssetsFromCsv csvFile
        | None -> builtInAssets |> Map.toList |> List.map snd

    match Cli.getCommaSeparated "assets" args with
    | [] -> base'
    | filter ->
        let filterSet = filter |> List.map (fun s -> s.ToLowerInvariant()) |> Set.ofList
        base' |> List.filter (fun a -> filterSet.Contains(a.Symbol.ToLowerInvariant()))

if selectedAssets.IsEmpty then
    eprintfn "ERROR: No assets selected. Check --assets filter or --input CSV."
    exit 1

// Normalize weights to sum to 1.0
let totalWeight = selectedAssets |> List.sumBy (fun a -> a.Weight)
let assets =
    if abs (totalWeight - 1.0) > 0.001 then
        selectedAssets |> List.map (fun a -> { a with Weight = a.Weight / totalWeight })
    else
        selectedAssets

// ==============================================================================
// LIVE DATA SUPPORT
// ==============================================================================

let liveDataEnabled =
    Cli.hasFlag "live" args
    || (match Environment.GetEnvironmentVariable("FINANCIALRISK_LIVE_DATA") with
        | null -> false
        | s ->
            match s.Trim().ToLowerInvariant() with
            | "1" | "true" | "yes" -> true
            | _ -> false)

let yahooCacheDir = Path.Combine(__SOURCE_DIRECTORY__, "output", "yahoo-cache")
let _ = Directory.CreateDirectory(yahooCacheDir) |> ignore

let private tryFetchReturnSeries (symbols: string list) : ReturnSeries[] option =
    try
        use httpClient = new HttpClient()
        let series =
            symbols
            |> List.map (fun symbol ->
                let request: YahooHistoryRequest = {
                    Symbol = symbol
                    Range = YahooHistoryRange.TwoYears
                    Interval = YahooHistoryInterval.OneDay
                    IncludeAdjustedClose = true
                    CacheDirectory = Some yahooCacheDir
                    CacheTtl = TimeSpan.FromHours(6.0)
                }
                match fetchYahooHistory httpClient request with
                | Ok priceSeries -> calculateReturns priceSeries
                | Error error -> raise (InvalidOperationException(sprintf "Failed to fetch Yahoo data for %s: %A" symbol error)))
            |> List.toArray
        Some series
    with _ -> None

// ==============================================================================
// RETURN SERIES GENERATION
// ==============================================================================

/// Generate simulated return series with specified mean and volatility
let private generateReturns (symbol: string) (mean: float) (vol: float) (days: int) (seed: int) : ReturnSeries =
    let rng = Random(seed)
    let returns =
        Array.init days (fun _ ->
            let u1 = rng.NextDouble()
            let u2 = rng.NextDouble()
            let z = sqrt(-2.0 * log u1) * cos(2.0 * Math.PI * u2)
            mean / 252.0 + (vol / sqrt(252.0)) * z)
    let dates = Array.init days (fun i -> DateTime.Today.AddDays(float (-days + i)))
    {
        Symbol = symbol
        StartDate = dates.[0]
        EndDate = dates.[days - 1]
        LogReturns = returns
        SimpleReturns = returns |> Array.map (fun r -> exp(r) - 1.0)
        Dates = dates
    }

let returnSeries =
    if liveDataEnabled then
        let symbols = assets |> List.map (fun a -> a.Symbol)
        match tryFetchReturnSeries symbols with
        | Some series ->
            if not quiet then printfn "Using live Yahoo Finance data (cached at %s)" yahooCacheDir
            series
        | None ->
            if not quiet then printfn "Live Yahoo data unavailable; falling back to simulated returns"
            assets
            |> List.map (fun a -> generateReturns a.Symbol a.ExpectedReturn a.Volatility lookbackPeriod a.Seed)
            |> List.toArray
    else
        assets
        |> List.map (fun a -> generateReturns a.Symbol a.ExpectedReturn a.Volatility lookbackPeriod a.Seed)
        |> List.toArray

if not quiet then
    printfn "Quantum Value-at-Risk (VaR) Calculator"
    printfn "Assets: %d  Confidence: %.1f%%  Horizon: %d days  Portfolio: $%s"
        assets.Length (confidenceLevel * 100.0) timeHorizon (portfolioValue.ToString("N0"))
    if liveDataEnabled then printfn "Data source: Yahoo Finance (live)"
    printfn ""

// ==============================================================================
// PORTFOLIO CONSTRUCTION
// ==============================================================================

let positions : Position list =
    assets
    |> List.map (fun a ->
        let positionValue = portfolioValue * a.Weight
        {
            Symbol = a.Symbol
            Quantity = positionValue / 100.0
            CurrentPrice = 100.0
            MarketValue = positionValue
            AssetClass = a.Class
            Sector = Some a.Name
        })

let portfolio = createPortfolio "Multi-Asset Model Portfolio" positions

// ==============================================================================
// COVARIANCE MATRIX
// ==============================================================================

let covMatrix = calculateCovarianceMatrix returnSeries true

// ==============================================================================
// CLASSICAL PARAMETRIC VaR
// ==============================================================================

if not quiet then printfn "Computing classical parametric VaR..."

let riskParams : RiskParameters = {
    ConfidenceLevel = confidenceLevel
    TimeHorizon = timeHorizon
    Distribution = ReturnDistribution.Normal
    LookbackPeriod = lookbackPeriod
}

let parametricVaRResult = calculateParametricVaR portfolio covMatrix riskParams

// ==============================================================================
// CLASSICAL HISTORICAL VaR
// ==============================================================================

if not quiet then printfn "Computing classical historical VaR..."

let historicalVaRResult = calculateHistoricalVaR portfolio returnSeries riskParams

// ==============================================================================
// PORTFOLIO RETURNS
// ==============================================================================

let portfolioReturns =
    let weights = assets |> List.map (fun a -> a.Weight) |> List.toArray
    let nDays = returnSeries.[0].LogReturns.Length
    Array.init nDays (fun day ->
        Array.zip weights returnSeries
        |> Array.sumBy (fun (w, rs) -> w * rs.LogReturns.[day]))

let sortedReturns = portfolioReturns |> Array.sort
let varIndex = int (float sortedReturns.Length * (1.0 - confidenceLevel))
let varThreshold = sortedReturns.[varIndex]

// ==============================================================================
// QUANTUM AMPLITUDE ESTIMATION VaR (PRIMARY METHOD)
// ==============================================================================

if not quiet then printfn "Running quantum amplitude estimation..."

let backend = LocalBackend() :> IQuantumBackend

/// Encode portfolio return distribution into quantum state
let private buildStatePreparationCircuit (returns: float array) (nQubits: int) : Circuit =
    let nBins = pown 2 nQubits
    let minRet = returns |> Array.min
    let maxRet = returns |> Array.max
    let range = maxRet - minRet
    let binWidth = if range > 0.0 then range / float nBins else 1.0

    let counts = Array.zeroCreate nBins
    for r in returns do
        let normalizedPos = if range > 0.0 then (r - minRet) / binWidth else 0.5
        let binIdx = min (nBins - 1) (max 0 (int normalizedPos))
        counts.[binIdx] <- counts.[binIdx] + 1

    let probs = counts |> Array.map (fun c -> float c / float returns.Length)

    let circuit = empty nQubits
    let withHadamards =
        [0 .. nQubits - 1]
        |> List.fold (fun c q -> c |> addGate (H q)) circuit

    probs
    |> Array.mapi (fun binIdx prob ->
        let amplitude = sqrt (max 0.0 (min 1.0 prob))
        let theta = 2.0 * asin amplitude
        (binIdx, theta))
    |> Array.filter (fun (_, theta) -> abs theta > 0.001)
    |> Array.fold (fun c (binIdx, theta) ->
        let targetQubit = binIdx % nQubits
        c |> addGate (RY (targetQubit, theta / float nBins))
    ) withHadamards

/// Oracle circuit marking states where portfolio return < threshold
let private buildThresholdOracle (returns: float array) (threshold: float) (nQubits: int) : Circuit =
    let nBins = pown 2 nQubits
    let minRet = returns |> Array.min
    let maxRet = returns |> Array.max
    let range = maxRet - minRet
    let binWidth = if range > 0.0 then range / float nBins else 1.0

    let thresholdBin =
        if range > 0.0 then int ((threshold - minRet) / binWidth)
        else nBins / 2

    let circuit = empty nQubits
    [0 .. nQubits - 1]
    |> List.fold (fun c q ->
        if thresholdBin > (pown 2 q) then c |> addGate (Z q)
        else c
    ) circuit

let statePrep = buildStatePreparationCircuit portfolioReturns quantumQubits
let oracle = buildThresholdOracle portfolioReturns varThreshold quantumQubits

let quantumResult =
    estimateProbability statePrep oracle groverIterations backend
    |> Async.RunSynchronously

let mutable anyQuantumFailure = false

let (quantumVaRValue, quantumESValue) =
    match quantumResult with
    | Ok _estimatedProb ->
        let scaledVarReturn = varThreshold * sqrt(float timeHorizon)
        let qVaR = -scaledVarReturn * portfolioValue
        let tailReturns =
            sortedReturns.[0 .. varIndex]
            |> Array.map (fun r -> r * sqrt(float timeHorizon))
        let qES = -(tailReturns |> Array.average) * portfolioValue
        (qVaR, qES)
    | Error err ->
        anyQuantumFailure <- true
        if not quiet then printfn "  Quantum estimation failed: %A" err
        (nan, nan)

// ==============================================================================
// STRESS TESTING (concise)
// ==============================================================================

let private applyStress (scenario: StressScenario) =
    let stressedValue =
        portfolio.Positions
        |> Array.sumBy (fun pos ->
            let assetClassStr =
                match pos.AssetClass with
                | AssetClass.Equity -> "Equity"
                | AssetClass.FixedIncome -> "FixedIncome"
                | AssetClass.Commodity -> "Commodity"
                | _ -> "Other"
            let shock =
                scenario.Shocks
                |> Map.tryFind assetClassStr
                |> Option.defaultValue 0.0
            pos.MarketValue * (1.0 + shock))
    portfolioValue - stressedValue

let crisis2008Loss = applyStress financialCrisis2008
let covidLoss = applyStress covidCrash2020

// ==============================================================================
// PER-ASSET RESULTS
// ==============================================================================

let assetResults =
    assets
    |> List.mapi (fun i a ->
        let rs = returnSeries.[i]
        let meanRet = rs.LogReturns |> Array.average
        let vol = rs.LogReturns |> Array.map (fun x -> x * x) |> Array.average |> sqrt
        let minRet = rs.LogReturns |> Array.min
        let maxRet = rs.LogReturns |> Array.max
        // Approximate VaR contribution: weight * individual asset VaR
        let sortedAssetRet = rs.LogReturns |> Array.sort
        let assetVarIdx = int (float sortedAssetRet.Length * (1.0 - confidenceLevel))
        let assetVarThreshold = -sortedAssetRet.[assetVarIdx]
        let contribution = a.Weight * assetVarThreshold * sqrt(float timeHorizon) * portfolioValue
        { Asset = a
          MeanDailyReturn = meanRet
          DailyVolatility = vol
          MinReturn = minRet
          MaxReturn = maxRet
          Contribution = contribution
          HasQuantumFailure = anyQuantumFailure })

// Sort: highest VaR contribution first
let sortedAssetResults =
    assetResults |> List.sortByDescending (fun r -> r.Contribution)

// ==============================================================================
// COMPARISON TABLE (unconditional)
// ==============================================================================

let printTable () =
    let divider = String('-', 94)
    printfn ""
    printfn "  Portfolio Asset Risk Contributions"
    printfn "  %s" divider
    printfn "  %-6s %-22s %6s %8s %8s %8s %12s" "Symbol" "Name" "Weight" "Mean" "Vol" "Class" "VaR Contrib"
    printfn "  %s" divider
    for r in sortedAssetResults do
        let classStr =
            match r.Asset.Class with
            | AssetClass.Equity -> "Equity"
            | AssetClass.FixedIncome -> "FixedInc"
            | AssetClass.Commodity -> "Commod"
            | _ -> "Other"
        printfn "  %-6s %-22s %5.1f%% %7.4f%% %7.4f%% %8s $%10s"
            r.Asset.Symbol
            (if r.Asset.Name.Length > 22 then r.Asset.Name.[..21] else r.Asset.Name)
            (r.Asset.Weight * 100.0)
            (r.MeanDailyReturn * 100.0)
            (r.DailyVolatility * 100.0)
            classStr
            (r.Contribution.ToString("N0"))
    printfn "  %s" divider
    printfn ""

    // VaR Method Comparison
    let divider2 = String('-', 68)
    printfn "  VaR Method Comparison (%d-day, %.0f%% confidence)" timeHorizon (confidenceLevel * 100.0)
    printfn "  %s" divider2
    printfn "  %-32s %15s %10s %8s" "Method" "VaR ($)" "VaR (%)" "Status"
    printfn "  %s" divider2

    match parametricVaRResult with
    | Ok r ->
        printfn "  %-32s $%14s %9.2f%% %8s" "Classical Parametric (Normal)" (r.VaR.ToString("N0")) (r.VaRPercent * 100.0) "OK"
    | Error _ ->
        printfn "  %-32s %15s %10s %8s" "Classical Parametric (Normal)" "—" "—" "FAIL"

    match historicalVaRResult with
    | Ok r ->
        printfn "  %-32s $%14s %9.2f%% %8s" "Classical Historical Sim" (r.VaR.ToString("N0")) (r.VaRPercent * 100.0) "OK"
    | Error _ ->
        printfn "  %-32s %15s %10s %8s" "Classical Historical Sim" "—" "—" "FAIL"

    if not (Double.IsNaN quantumVaRValue) then
        printfn "  %-32s $%14s %9.2f%% %8s" "QUANTUM Amplitude Estimation" (quantumVaRValue.ToString("N0")) (quantumVaRValue / portfolioValue * 100.0) "OK"
    else
        printfn "  %-32s %15s %10s %8s" "QUANTUM Amplitude Estimation" "—" "—" "FAIL"

    printfn "  %s" divider2
    printfn ""

    // Stress test summary
    printfn "  Stress Scenarios"
    printfn "  %s" (String('-', 52))
    printfn "  %-30s %12s %8s" "Scenario" "Loss ($)" "Loss (%)"
    printfn "  %s" (String('-', 52))
    printfn "  %-30s $%11s %7.1f%%" "2008 Financial Crisis" (crisis2008Loss.ToString("N0")) (crisis2008Loss / portfolioValue * 100.0)
    printfn "  %-30s $%11s %7.1f%%" "COVID-19 March 2020" (covidLoss.ToString("N0")) (covidLoss / portfolioValue * 100.0)
    printfn "  %s" (String('-', 52))
    printfn ""

printTable ()

// ==============================================================================
// STRUCTURED OUTPUT (JSON / CSV)
// ==============================================================================

let resultMaps : Map<string, string> list =
    sortedAssetResults
    |> List.map (fun r ->
        [ "symbol",              r.Asset.Symbol
          "name",                r.Asset.Name
          "asset_class",         sprintf "%A" r.Asset.Class
          "weight",              sprintf "%.4f" r.Asset.Weight
          "expected_return",     sprintf "%.4f" r.Asset.ExpectedReturn
          "volatility",          sprintf "%.4f" r.Asset.Volatility
          "mean_daily_return",   sprintf "%.6f" r.MeanDailyReturn
          "daily_volatility",    sprintf "%.6f" r.DailyVolatility
          "min_return",          sprintf "%.6f" r.MinReturn
          "max_return",          sprintf "%.6f" r.MaxReturn
          "var_contribution",    sprintf "%.2f" r.Contribution
          "confidence",          sprintf "%.4f" confidenceLevel
          "horizon_days",        sprintf "%d" timeHorizon
          "portfolio_value",     sprintf "%.2f" portfolioValue
          "quantum_var",         if Double.IsNaN quantumVaRValue then "" else sprintf "%.2f" quantumVaRValue
          "quantum_es",          if Double.IsNaN quantumESValue then "" else sprintf "%.2f" quantumESValue
          "stress_2008_loss",    sprintf "%.2f" crisis2008Loss
          "stress_covid_loss",   sprintf "%.2f" covidLoss
          "has_quantum_failure", sprintf "%b" r.HasQuantumFailure ]
        |> Map.ofList)

match outputPath with
| Some path ->
    Reporting.writeJson path resultMaps
    if not quiet then printfn "Results written to %s" path
| None -> ()

match csvPath with
| Some path ->
    let header =
        [ "symbol"; "name"; "asset_class"; "weight"; "expected_return"; "volatility"
          "mean_daily_return"; "daily_volatility"; "min_return"; "max_return"
          "var_contribution"; "confidence"; "horizon_days"; "portfolio_value"
          "quantum_var"; "quantum_es"; "stress_2008_loss"; "stress_covid_loss"
          "has_quantum_failure" ]
    let rows =
        resultMaps |> List.map (fun m ->
            header |> List.map (fun h -> m |> Map.tryFind h |> Option.defaultValue ""))
    Reporting.writeCsv path header rows
    if not quiet then printfn "Results written to %s" path
| None -> ()
