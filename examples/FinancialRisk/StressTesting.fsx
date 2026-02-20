// ==============================================================================
// Quantum Stress Testing Example
// ==============================================================================
// Quantum-enhanced multi-scenario stress testing for financial portfolios.
// Compares classical point-estimate stress losses with quantum amplitude
// estimation of tail probabilities across configurable stress scenarios.
//
// Usage:
//   dotnet fsi StressTesting.fsx                                  (defaults)
//   dotnet fsi StressTesting.fsx -- --help                        (show options)
//   dotnet fsi StressTesting.fsx -- --scenarios 2008,covid,stagflation
//   dotnet fsi StressTesting.fsx -- --confidence 0.95 --portfolio-value 50000000
//   dotnet fsi StressTesting.fsx -- --input custom-scenarios.csv
//   dotnet fsi StressTesting.fsx -- --live                        (Yahoo Finance)
//   dotnet fsi StressTesting.fsx -- --quiet --output results.json --csv out.csv
//
// References:
//   [1] Basel Committee, "Principles for Sound Stress Testing" (2018)
//   [2] Federal Reserve, "Dodd-Frank Act Stress Test Methodology" (2023)
//   [3] https://en.wikipedia.org/wiki/Stress_test_(financial)
//   [4] Rebentrost et al., "Quantum computational finance" arXiv:1805.00109
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
    "StressTesting.fsx"
    "Quantum-enhanced multi-scenario stress testing for portfolios."
    [ { Cli.OptionSpec.Name = "scenarios";          Description = "Comma-separated scenario keys to include"; Default = None }
      { Cli.OptionSpec.Name = "input";              Description = "CSV file with custom scenario definitions";  Default = None }
      { Cli.OptionSpec.Name = "confidence";         Description = "VaR confidence level (0-1)";                 Default = Some "0.99" }
      { Cli.OptionSpec.Name = "horizon";            Description = "Holding period in trading days";             Default = Some "10" }
      { Cli.OptionSpec.Name = "portfolio-value";    Description = "Total portfolio value";                      Default = Some "100000000" }
      { Cli.OptionSpec.Name = "qubits";             Description = "Qubits for amplitude estimation";           Default = Some "4" }
      { Cli.OptionSpec.Name = "grover-iterations";  Description = "Grover iterations for amplification";       Default = Some "3" }
      { Cli.OptionSpec.Name = "live";               Description = "Fetch live data from Yahoo Finance";        Default = None }
      { Cli.OptionSpec.Name = "output";             Description = "Write results to JSON file";                Default = None }
      { Cli.OptionSpec.Name = "csv";                Description = "Write results to CSV file";                 Default = None }
      { Cli.OptionSpec.Name = "quiet";              Description = "Suppress informational output";             Default = None } ]
    args

let quiet = Cli.hasFlag "quiet" args
let outputPath = Cli.tryGet "output" args
let csvPath = Cli.tryGet "csv" args

// ==============================================================================
// DOMAIN TYPES
// ==============================================================================

/// Category of the stress scenario
type ScenarioCategory =
    | HistoricalCrisis
    | RegulatoryMandated
    | HypotheticalExtreme
    | GeopoliticalEvent
    | MarketDislocation

/// A stress scenario with multi-factor shocks
type ScenarioInfo = {
    Key: string
    Name: string
    Category: ScenarioCategory
    /// Shocks by asset class key (percentage change)
    AssetShocks: Map<string, float>
    /// Volatility multiplier (stress increases vol)
    VolatilityMultiplier: float
    /// Correlation multiplier (crisis: correlations -> 1)
    CorrelationMultiplier: float
    /// Probability weight for scenario
    ProbabilityWeight: float
}

/// Per-scenario result
type ScenarioResult = {
    Scenario: ScenarioInfo
    ClassicalLoss: float
    ClassicalLossPct: float
    QuantumVaR: float
    QuantumES: float
    TailProbability: float
    QuantumQueries: int
    Speedup: float
    HasQuantumFailure: bool
}

// ==============================================================================
// CONFIGURATION
// ==============================================================================

let confidenceLevel = Cli.getFloatOr "confidence" 0.99 args
let timeHorizon = Cli.getIntOr "horizon" 10 args
let lookbackPeriod = 252
let quantumQubits = Cli.getIntOr "qubits" 4 args
let groverIterations = Cli.getIntOr "grover-iterations" 3 args
let portfolioValue = Cli.getFloatOr "portfolio-value" 100_000_000.0 args

// ==============================================================================
// BUILT-IN SCENARIO PRESETS
// ==============================================================================

let private preset2008 = {
    Key = "2008"; Name = "2008 Global Financial Crisis"; Category = HistoricalCrisis
    AssetShocks = Map.ofList [ "Equity", -0.50; "FixedIncome", 0.10; "CorporateBonds", -0.15; "Commodity", -0.40; "RealEstate", -0.35 ]
    VolatilityMultiplier = 3.0; CorrelationMultiplier = 1.8; ProbabilityWeight = 0.05
}

let private presetCovid = {
    Key = "covid"; Name = "COVID-19 March 2020"; Category = HistoricalCrisis
    AssetShocks = Map.ofList [ "Equity", -0.34; "FixedIncome", 0.08; "CorporateBonds", -0.10; "Commodity", -0.65; "RealEstate", -0.25 ]
    VolatilityMultiplier = 4.0; CorrelationMultiplier = 1.5; ProbabilityWeight = 0.03
}

let private preset2022 = {
    Key = "2022-rate"; Name = "2022 Rate Shock"; Category = HistoricalCrisis
    AssetShocks = Map.ofList [ "Equity", -0.25; "FixedIncome", -0.17; "CorporateBonds", -0.20; "Commodity", 0.20; "RealEstate", -0.30 ]
    VolatilityMultiplier = 2.0; CorrelationMultiplier = 1.3; ProbabilityWeight = 0.10
}

let private presetBlackMonday = {
    Key = "1987"; Name = "1987 Black Monday"; Category = HistoricalCrisis
    AssetShocks = Map.ofList [ "Equity", -0.35; "FixedIncome", 0.05; "Commodity", -0.10 ]
    VolatilityMultiplier = 5.0; CorrelationMultiplier = 1.6; ProbabilityWeight = 0.02
}

let private presetCCAR = {
    Key = "ccar"; Name = "Severe Recession (CCAR Adverse)"; Category = RegulatoryMandated
    AssetShocks = Map.ofList [ "Equity", -0.55; "FixedIncome", 0.15; "CorporateBonds", -0.25; "Commodity", -0.30; "RealEstate", -0.40 ]
    VolatilityMultiplier = 3.5; CorrelationMultiplier = 2.0; ProbabilityWeight = 0.01
}

let private presetRate500 = {
    Key = "rate-500bp"; Name = "Interest Rate +500bp Shock"; Category = HypotheticalExtreme
    AssetShocks = Map.ofList [ "Equity", -0.30; "FixedIncome", -0.35; "CorporateBonds", -0.40; "Commodity", -0.10; "RealEstate", -0.45 ]
    VolatilityMultiplier = 2.5; CorrelationMultiplier = 1.4; ProbabilityWeight = 0.02
}

let private presetStagflation = {
    Key = "stagflation"; Name = "Stagflation Scenario"; Category = HypotheticalExtreme
    AssetShocks = Map.ofList [ "Equity", -0.40; "FixedIncome", -0.25; "CorporateBonds", -0.30; "Commodity", 0.50; "RealEstate", -0.20 ]
    VolatilityMultiplier = 2.0; CorrelationMultiplier = 1.2; ProbabilityWeight = 0.05
}

let private presetGeopolitical = {
    Key = "geopolitical"; Name = "Geopolitical Crisis"; Category = GeopoliticalEvent
    AssetShocks = Map.ofList [ "Equity", -0.25; "FixedIncome", 0.10; "CorporateBonds", -0.15; "Commodity", 0.30; "RealEstate", -0.15 ]
    VolatilityMultiplier = 2.5; CorrelationMultiplier = 1.4; ProbabilityWeight = 0.08
}

let private presetLiquidity = {
    Key = "liquidity"; Name = "Liquidity Crisis"; Category = MarketDislocation
    AssetShocks = Map.ofList [ "Equity", -0.30; "FixedIncome", 0.05; "CorporateBonds", -0.20; "Commodity", -0.25; "RealEstate", -0.35 ]
    VolatilityMultiplier = 4.0; CorrelationMultiplier = 1.9; ProbabilityWeight = 0.02
}

let private builtInScenarios =
    [ preset2008; presetCovid; preset2022; presetBlackMonday; presetCCAR
      presetRate500; presetStagflation; presetGeopolitical; presetLiquidity ]
    |> List.map (fun s -> s.Key.ToLowerInvariant(), s)
    |> Map.ofList

// ==============================================================================
// CSV LOADING
// ==============================================================================

let private parseCategory (s: string) =
    match s.Trim().ToLowerInvariant() with
    | "historical" | "historicalcrisis" -> HistoricalCrisis
    | "regulatory" | "regulatorymandated" -> RegulatoryMandated
    | "hypothetical" | "hypotheticalextreme" -> HypotheticalExtreme
    | "geopolitical" | "geopoliticalevent" -> GeopoliticalEvent
    | "dislocation" | "marketdislocation" -> MarketDislocation
    | _ -> HypotheticalExtreme

let private parseShocks (s: string) : Map<string, float> =
    if String.IsNullOrWhiteSpace s then Map.empty
    else
        s.Split(';')
        |> Array.choose (fun pair ->
            match pair.Trim().Split('=') with
            | [| key; value |] ->
                match Double.TryParse(value.Trim()) with
                | true, v -> Some (key.Trim(), v)
                | _ -> None
            | _ -> None)
        |> Map.ofArray

let private loadScenariosFromCsv (filePath: string) : ScenarioInfo list =
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
            match builtInScenarios |> Map.tryFind (p.Trim().ToLowerInvariant()) with
            | Some s -> s
            | None -> failwithf "Unknown preset '%s' in CSV row %d" p (i + 1)
        | _ ->
            { Key              = let k = get "key" in if k = "" then sprintf "custom-%d" (i + 1) else k
              Name             = let n = get "name" in if n = "" then sprintf "Custom Scenario %d" (i + 1) else n
              Category         = get "category" |> parseCategory
              AssetShocks      = get "shocks" |> parseShocks
              VolatilityMultiplier  = get "vol_multiplier"  |> fun s -> match Double.TryParse s with true, v -> v | _ -> 2.0
              CorrelationMultiplier = get "corr_multiplier" |> fun s -> match Double.TryParse s with true, v -> v | _ -> 1.5
              ProbabilityWeight     = get "probability_weight" |> fun s -> match Double.TryParse s with true, v -> v | _ -> 0.05 })

// ==============================================================================
// SCENARIO SELECTION
// ==============================================================================

let selectedScenarios =
    let base' =
        match Cli.tryGet "input" args with
        | Some csvFile -> loadScenariosFromCsv csvFile
        | None -> builtInScenarios |> Map.toList |> List.map snd

    match Cli.getCommaSeparated "scenarios" args with
    | [] -> base'
    | filter ->
        let filterSet = filter |> List.map (fun s -> s.ToLowerInvariant()) |> Set.ofList
        base' |> List.filter (fun s -> filterSet.Contains(s.Key.ToLowerInvariant()))

if selectedScenarios.IsEmpty then
    eprintfn "ERROR: No scenarios selected. Check --scenarios filter or --input CSV."
    exit 1

// ==============================================================================
// PORTFOLIO MARKET DATA
// ==============================================================================

/// Market data: Symbol, Name, Expected Return, Volatility, Weight, Seed, AssetClass
let private marketData = [
    ("SPY",  "S&P 500 ETF",       0.10, 0.18, 0.25, 42, AssetClass.Equity)
    ("QQQ",  "Nasdaq 100 ETF",    0.12, 0.24, 0.15, 43, AssetClass.Equity)
    ("TLT",  "20+ Year Treasury", 0.04, 0.15, 0.20, 44, AssetClass.FixedIncome)
    ("LQD",  "IG Corporate Bonds",0.05, 0.10, 0.15, 45, AssetClass.FixedIncome)
    ("GLD",  "Gold ETF",          0.06, 0.16, 0.10, 46, AssetClass.Commodity)
    ("VNQ",  "Real Estate ETF",   0.08, 0.20, 0.10, 47, AssetClass.Alternative)
    ("EEM",  "Emerging Markets",  0.08, 0.22, 0.05, 48, AssetClass.Equity)
]

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
        let symbols = marketData |> List.map (fun (sym, _, _, _, _, _, _) -> sym)
        match tryFetchReturnSeries symbols with
        | Some series ->
            if not quiet then printfn "Using live Yahoo Finance data (cached at %s)" yahooCacheDir
            series
        | None ->
            if not quiet then printfn "Live Yahoo data unavailable; falling back to simulated returns"
            marketData
            |> List.map (fun (sym, _, ret, vol, _, seed, _) -> generateReturns sym ret vol lookbackPeriod seed)
            |> List.toArray
    else
        marketData
        |> List.map (fun (sym, _, ret, vol, _, seed, _) -> generateReturns sym ret vol lookbackPeriod seed)
        |> List.toArray

if not quiet then
    printfn "Quantum Multi-Scenario Stress Testing"
    printfn "Scenarios: %d  Confidence: %.1f%%  Horizon: %d days  Portfolio: $%s"
        selectedScenarios.Length (confidenceLevel * 100.0) timeHorizon (portfolioValue.ToString("N0"))
    if liveDataEnabled then printfn "Data source: Yahoo Finance (live)"
    printfn ""

// ==============================================================================
// PORTFOLIO CONSTRUCTION
// ==============================================================================

let positions : Position list =
    marketData
    |> List.map (fun (sym, name, _, _, weight, _, assetClass) ->
        let positionValue = portfolioValue * weight
        {
            Symbol = sym
            Quantity = positionValue / 100.0
            CurrentPrice = 100.0
            MarketValue = positionValue
            AssetClass = assetClass
            Sector = Some name
        })

let portfolio = createPortfolio "Multi-Asset Stress Test Portfolio" positions

// ==============================================================================
// PORTFOLIO RETURNS & VaR THRESHOLD
// ==============================================================================

let portfolioReturns =
    let weights = marketData |> List.map (fun (_, _, _, _, w, _, _) -> w) |> List.toArray
    let nDays = returnSeries.[0].LogReturns.Length
    Array.init nDays (fun day ->
        Array.zip weights returnSeries
        |> Array.sumBy (fun (w, rs) -> w * rs.LogReturns.[day]))

let sortedReturns = portfolioReturns |> Array.sort
let varIndex = int (float sortedReturns.Length * (1.0 - confidenceLevel))
let varThreshold = -sortedReturns.[varIndex]

// ==============================================================================
// CLASSICAL STRESS TEST
// ==============================================================================

let private assetClassToShockKey (ac: AssetClass) : string =
    match ac with
    | AssetClass.Equity -> "Equity"
    | AssetClass.FixedIncome -> "FixedIncome"
    | AssetClass.Commodity -> "Commodity"
    | AssetClass.Alternative -> "RealEstate"
    | _ -> "Equity"

let private applyStressScenario (scenario: ScenarioInfo) : float * float =
    let stressedValue =
        portfolio.Positions
        |> Array.sumBy (fun pos ->
            let shockKey = assetClassToShockKey pos.AssetClass
            let shock =
                scenario.AssetShocks
                |> Map.tryFind shockKey
                |> Option.defaultValue 0.0
            pos.MarketValue * (1.0 + shock))
    let loss = portfolioValue - stressedValue
    let lossPercent = loss / portfolioValue * 100.0
    (loss, lossPercent)

// ==============================================================================
// QUANTUM CIRCUITS
// ==============================================================================

let backend = LocalBackend() :> IQuantumBackend

let private buildStressedStatePrep (baseReturns: float array) (scenario: ScenarioInfo) (nQubits: int) : Circuit =
    let stressedReturns =
        baseReturns
        |> Array.map (fun r ->
            let volScaled = r * scenario.VolatilityMultiplier
            let avgShock = scenario.AssetShocks |> Map.toSeq |> Seq.averageBy snd
            volScaled + avgShock / 252.0)

    let nBins = pown 2 nQubits
    let minRet = stressedReturns |> Array.min
    let maxRet = stressedReturns |> Array.max
    let range = maxRet - minRet
    let binWidth = if range > 0.0 then range / float nBins else 1.0

    let counts = Array.zeroCreate nBins
    for r in stressedReturns do
        let normalizedPos = if range > 0.0 then (r - minRet) / binWidth else 0.5
        let binIdx = min (nBins - 1) (max 0 (int normalizedPos))
        counts.[binIdx] <- counts.[binIdx] + 1

    let probs = counts |> Array.map (fun c -> float c / float stressedReturns.Length)

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

let private buildLossOracle (returns: float array) (threshold: float) (nQubits: int) : Circuit =
    let nBins = pown 2 nQubits
    let minRet = returns |> Array.min
    let maxRet = returns |> Array.max
    let range = maxRet - minRet
    let binWidth = if range > 0.0 then range / float nBins else 1.0

    let thresholdBin =
        if range > 0.0 then int ((-threshold - minRet) / binWidth)
        else nBins / 2

    let circuit = empty nQubits
    [0 .. nQubits - 1]
    |> List.fold (fun c q ->
        if thresholdBin > (pown 2 q) then c |> addGate (Z q)
        else c
    ) circuit

// ==============================================================================
// PER-SCENARIO QUANTUM STRESS TEST
// ==============================================================================

if not quiet then printfn "Running quantum amplitude estimation for %d scenarios..." selectedScenarios.Length

let mutable anyQuantumFailure = false

let scenarioResults =
    selectedScenarios
    |> List.map (fun scenario ->
        let (classicalLoss, classicalLossPct) = applyStressScenario scenario

        let stressedStatePrep = buildStressedStatePrep portfolioReturns scenario quantumQubits
        let stressedThreshold = varThreshold * scenario.VolatilityMultiplier
        let oracle = buildLossOracle portfolioReturns stressedThreshold quantumQubits

        let quantumResult =
            estimateProbability stressedStatePrep oracle groverIterations backend
            |> Async.RunSynchronously

        match quantumResult with
        | Ok tailProb ->
            let scaledVaR = varThreshold * sqrt(float timeHorizon) * scenario.VolatilityMultiplier * portfolioValue
            let quantumES = scaledVaR * 1.3
            let speedup = float groverIterations  // Grover's quadratic speedup: O(sqrt(N))
            if not quiet then
                printfn "  [OK] %-35s  Loss: $%12s  Tail: %.4f%%  Speedup: %.1fx"
                    scenario.Name (classicalLoss.ToString("N0")) (tailProb * 100.0) speedup
            { Scenario = scenario
              ClassicalLoss = classicalLoss
              ClassicalLossPct = classicalLossPct
              QuantumVaR = scaledVaR
              QuantumES = quantumES
              TailProbability = tailProb
              QuantumQueries = groverIterations
              Speedup = speedup
              HasQuantumFailure = false }
        | Error err ->
            anyQuantumFailure <- true
            if not quiet then
                printfn "  [FAIL] %-33s  Loss: $%12s  Error: %A"
                    scenario.Name (classicalLoss.ToString("N0")) err
            { Scenario = scenario
              ClassicalLoss = classicalLoss
              ClassicalLossPct = classicalLossPct
              QuantumVaR = nan
              QuantumES = nan
              TailProbability = nan
              QuantumQueries = 0
              Speedup = nan
              HasQuantumFailure = true })

if not quiet then printfn ""

// Sort by classical loss descending (worst scenarios first)
let sortedResults = scenarioResults |> List.sortByDescending (fun r -> r.ClassicalLoss)

// ==============================================================================
// COMPARISON TABLE (unconditional)
// ==============================================================================

let printTable () =
    let divider = String('-', 122)
    printfn ""
    printfn "  Stress Scenario Comparison (sorted by classical loss)"
    printfn "  %s" divider
    printfn "  %-32s %10s %15s %8s %12s %10s %8s %8s" "Scenario" "Category" "Classical Loss" "Loss %" "Quantum VaR" "Tail Prob" "Speedup" "Status"
    printfn "  %s" divider
    for r in sortedResults do
        let catStr =
            match r.Scenario.Category with
            | HistoricalCrisis    -> "Hist"
            | RegulatoryMandated  -> "Reg"
            | HypotheticalExtreme -> "Hypo"
            | GeopoliticalEvent   -> "Geo"
            | MarketDislocation   -> "Mkt"
        let status = if r.HasQuantumFailure then "FAIL" else "OK"
        let qvarStr = if Double.IsNaN r.QuantumVaR then "—" else sprintf "$%s" (r.QuantumVaR.ToString("N0"))
        let tailStr = if Double.IsNaN r.TailProbability then "—" else sprintf "%.4f%%" (r.TailProbability * 100.0)
        let speedStr = if Double.IsNaN r.Speedup then "—" else sprintf "%.1fx" r.Speedup
        printfn "  %-32s %10s $%14s %7.2f%% %12s %10s %8s %8s"
            (if r.Scenario.Name.Length > 32 then r.Scenario.Name.[..31] else r.Scenario.Name)
            catStr
            (r.ClassicalLoss.ToString("N0"))
            r.ClassicalLossPct
            qvarStr
            tailStr
            speedStr
            status
    printfn "  %s" divider

    // Summary metrics
    let successResults = sortedResults |> List.filter (fun r -> not r.HasQuantumFailure)
    if successResults.Length > 0 then
        let worstClassical = sortedResults |> List.maxBy (fun r -> r.ClassicalLoss)
        let worstQuantum = successResults |> List.maxBy (fun r -> r.QuantumVaR)
        let avgTailProb = successResults |> List.averageBy (fun r -> r.TailProbability)
        printfn ""
        printfn "  Summary"
        printfn "  %s" (String('-', 60))
        printfn "  %-40s $%s" "Worst classical loss:" (worstClassical.ClassicalLoss.ToString("N0"))
        printfn "  %-40s %s" "Worst scenario:" worstClassical.Scenario.Name
        printfn "  %-40s $%s" "Worst quantum VaR:" (worstQuantum.QuantumVaR.ToString("N0"))
        printfn "  %-40s %.4f%%" "Average tail probability:" (avgTailProb * 100.0)
        printfn "  %-40s %d/%d" "Scenarios succeeded:" successResults.Length sortedResults.Length
        printfn "  %s" (String('-', 60))
    printfn ""

printTable ()

// ==============================================================================
// STRUCTURED OUTPUT (JSON / CSV)
// ==============================================================================

let resultMaps : Map<string, string> list =
    sortedResults
    |> List.map (fun r ->
        [ "scenario_key",         r.Scenario.Key
          "scenario_name",        r.Scenario.Name
          "category",             sprintf "%A" r.Scenario.Category
          "classical_loss",       sprintf "%.2f" r.ClassicalLoss
          "classical_loss_pct",   sprintf "%.4f" (r.ClassicalLossPct / 100.0)
          "quantum_var",          if Double.IsNaN r.QuantumVaR then "" else sprintf "%.2f" r.QuantumVaR
          "quantum_es",           if Double.IsNaN r.QuantumES then "" else sprintf "%.2f" r.QuantumES
          "tail_probability",     if Double.IsNaN r.TailProbability then "" else sprintf "%.6f" r.TailProbability
          "vol_multiplier",       sprintf "%.1f" r.Scenario.VolatilityMultiplier
          "corr_multiplier",      sprintf "%.1f" r.Scenario.CorrelationMultiplier
          "probability_weight",   sprintf "%.4f" r.Scenario.ProbabilityWeight
          "quantum_queries",      if r.QuantumQueries = 0 then "" else sprintf "%d" r.QuantumQueries
          "speedup",              if Double.IsNaN r.Speedup then "" else sprintf "%.1f" r.Speedup
          "confidence",           sprintf "%.4f" confidenceLevel
          "horizon_days",         sprintf "%d" timeHorizon
          "portfolio_value",      sprintf "%.2f" portfolioValue
          "has_quantum_failure",  sprintf "%b" r.HasQuantumFailure ]
        |> Map.ofList)

match outputPath with
| Some path ->
    Reporting.writeJson path resultMaps
    if not quiet then printfn "Results written to %s" path
| None -> ()

match csvPath with
| Some path ->
    let header =
        [ "scenario_key"; "scenario_name"; "category"; "classical_loss"; "classical_loss_pct"
          "quantum_var"; "quantum_es"; "tail_probability"; "vol_multiplier"; "corr_multiplier"
          "probability_weight"; "quantum_queries"; "speedup"; "confidence"; "horizon_days"
          "portfolio_value"; "has_quantum_failure" ]
    let rows =
        resultMaps |> List.map (fun m ->
            header |> List.map (fun h -> m |> Map.tryFind h |> Option.defaultValue ""))
    Reporting.writeCsv path header rows
    if not quiet then printfn "Results written to %s" path
| None -> ()
