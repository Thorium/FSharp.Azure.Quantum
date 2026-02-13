// ==============================================================================
// Regime-Aware Portfolio Optimization
// ==============================================================================
// Adaptive portfolio optimization using Hidden Markov Models (HMM) to detect
// market regimes (Bull/Bear) and applying regime-specific strategies.
//
// Business Context:
// Markets switch between regimes (low-volatility growth vs. high-volatility crash).
// A static "mean-variance" portfolio often fails during regime shifts.
// This example detects the current regime and re-optimizes using a quantum-ready
// solver with regime-specific constraints.
//
// Workflow:
// 1. Generate synthetic market data (S&P 500 proxy)
// 2. Train/Apply HMM (Viterbi) to classify current market state
// 3. Select strategy: Bull -> Maximize Growth, Bear -> Capital Preservation
// 4. Optimize portfolio using HybridSolver (Quantum-Ready)
//
// Usage:
//   dotnet fsi RegimeAwarePortfolio.fsx
//   dotnet fsi RegimeAwarePortfolio.fsx -- --budget 200000 --days 500
//   dotnet fsi RegimeAwarePortfolio.fsx -- --quiet --output results.json --csv results.csv
// ==============================================================================

#r "../../src/FSharp.Azure.Quantum/bin/Debug/net10.0/FSharp.Azure.Quantum.dll"
#load "../_common/Cli.fs"
#load "../_common/Data.fs"
#load "../_common/Reporting.fs"
open FSharp.Azure.Quantum.Examples.Common

open System
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Backends.LocalBackend
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.Classical

// --- CLI ---
let argv = fsi.CommandLineArgs |> Array.skip 1
let args = Cli.parse argv
Cli.exitIfHelp "RegimeAwarePortfolio.fsx" "HMM regime detection + quantum portfolio optimization" [
    { Name = "budget"; Description = "Investment budget in dollars"; Default = Some "100000" }
    { Name = "days"; Description = "Days of market data to generate"; Default = Some "252" }
    { Name = "seed"; Description = "Random seed for data generation"; Default = Some "42" }
    { Name = "output"; Description = "Write results to JSON file"; Default = None }
    { Name = "csv"; Description = "Write results to CSV file"; Default = None }
    { Name = "quiet"; Description = "Suppress printed output"; Default = None }
] args

let cliBudget = Cli.getFloatOr "budget" 100000.0 args
let cliDays = Cli.getIntOr "days" 252 args
let cliSeed = Cli.getIntOr "seed" 42 args
let quiet = Cli.hasFlag "quiet" args
let outputPath = Cli.tryGet "output" args
let csvPath = Cli.tryGet "csv" args

let pr fmt = Printf.ksprintf (fun s -> if not quiet then printfn "%s" s) fmt

// ==============================================================================
// Domain Model
// ==============================================================================

type MarketRegime =
    | Bull
    | Bear

type Stock = {
    Symbol: string
    Name: string
    CurrentPrice: float
}

type PortfolioAnalysis = {
    Allocations: (string * float * float) list
    TotalValue: float
    ExpectedReturn: float
    Risk: float
    SharpeRatio: float
    DiversificationScore: int
}

// ==============================================================================
// Synthetic Data Generation (Markov chain - inherently stateful)
// ==============================================================================

let generateMarketData (days: int) (seed: int) (stockList: Stock list) =
    let rng = Random(seed)
    let p_bull_bear = 0.05
    let p_bear_bull = 0.10

    // Markov chain state evolution requires mutable state (sequential random process)
    let mutable state = Bull
    let marketReturns = Array.zeroCreate days
    let regimes = Array.zeroCreate days

    let mutable assetReturns =
        stockList |> List.map (fun a -> a.Symbol, Array.zeroCreate<float> days) |> Map.ofList

    let assetParams =
        [
            ("TQQQ", ((0.0015, 0.02), (-0.003, 0.05)))
            ("AAPL", ((0.001, 0.012), (-0.001, 0.025)))
            ("MSFT", ((0.0009, 0.011), (-0.001, 0.022)))
            ("JNJ",  ((0.0003, 0.008), (-0.0005, 0.01)))
            ("XLP",  ((0.0002, 0.007), (-0.0002, 0.008)))
            ("GLD",  ((0.0002, 0.009), (0.0006, 0.012)))
            ("TLT",  ((0.0001, 0.006), (0.0004, 0.008)))
            ("SH",   ((-0.0005, 0.012), (0.0015, 0.025)))
        ] |> Map.ofList

    for i in 0 .. days - 1 do
        if state = Bull && rng.NextDouble() < p_bull_bear then state <- Bear
        elif state = Bear && rng.NextDouble() < p_bear_bull then state <- Bull

        regimes.[i] <- state

        let (m_mu, m_sigma) = if state = Bull then (0.0005, 0.01) else (-0.001, 0.03)
        let u1 = rng.NextDouble()
        let u2 = rng.NextDouble()
        let z = sqrt(-2.0 * log u1) * cos(2.0 * Math.PI * u2)
        marketReturns.[i] <- m_mu + m_sigma * z

        stockList |> List.iter (fun asset ->
            let (bullP, bearP) = assetParams.[asset.Symbol]
            let (mu, sigma) = if state = Bull then bullP else bearP
            let u1_a = rng.NextDouble()
            let u2_a = rng.NextDouble()
            let z_a = sqrt(-2.0 * log u1_a) * cos(2.0 * Math.PI * u2_a)
            let arr = assetReturns.[asset.Symbol]
            arr.[i] <- mu + sigma * z_a
        )

    (marketReturns, regimes, assetReturns)

// ==============================================================================
// Asset Universe
// ==============================================================================

let assets = [
    { Symbol = "TQQQ"; Name = "Tech Aggressive"; CurrentPrice = 50.0 }
    { Symbol = "AAPL"; Name = "Apple"; CurrentPrice = 175.0 }
    { Symbol = "MSFT"; Name = "Microsoft"; CurrentPrice = 380.0 }
    { Symbol = "JNJ"; Name = "Johnson&Johnson"; CurrentPrice = 160.0 }
    { Symbol = "XLP"; Name = "Consumer Staples"; CurrentPrice = 75.0 }
    { Symbol = "GLD"; Name = "Gold"; CurrentPrice = 190.0 }
    { Symbol = "TLT"; Name = "Treasury Bonds"; CurrentPrice = 95.0 }
    { Symbol = "SH"; Name = "Short S&P500"; CurrentPrice = 15.0 }
]

// ==============================================================================
// Hidden Markov Model (HMM) - Viterbi Algorithm
// ==============================================================================

module MarketHMM =

    let gaussianPdf x mu sigma =
        let coeff = 1.0 / (sigma * sqrt(2.0 * Math.PI))
        let exponent = -0.5 * ((x - mu) / sigma) ** 2.0
        coeff * exp exponent

    // Viterbi algorithm for 2-state HMM (inherently stateful with Array2D)
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
// Regime-Aware Optimizer
// ==============================================================================

module RegimeAwareOptimizer =

    let calculateStats (returns: float[]) =
        let mean = Array.average returns
        let sumSq = returns |> Array.sumBy (fun r -> pown (r - mean) 2)
        let vol = sqrt (sumSq / float returns.Length)
        (mean, vol)

    let toSolverAsset (history: Map<string, float[]>) (s: Stock) : PortfolioSolver.Asset =
        let recentReturns =
            match history.TryFind s.Symbol with
            | Some r -> r |> Array.skip (max 0 (r.Length - 30))
            | None -> [| 0.0 |]
        let (mu, sigma) = calculateStats recentReturns
        { Symbol = s.Symbol; ExpectedReturn = mu; Risk = sigma; Price = s.CurrentPrice }

    let optimize (regime: MarketRegime)
                 (budget: float)
                 (qBackend: IQuantumBackend)
                 (assetHistory: Map<string, float[]>)
                 : Result<PortfolioAnalysis, string> =

        let solverAssets = assets |> List.map (toSolverAsset assetHistory)

        let constraints =
            match regime with
            | Bull ->
                { PortfolioSolver.Constraints.Budget = budget
                  PortfolioSolver.Constraints.MinHolding = 0.0
                  PortfolioSolver.Constraints.MaxHolding = budget * 0.4 }
            | Bear ->
                { PortfolioSolver.Constraints.Budget = budget
                  PortfolioSolver.Constraints.MinHolding = 0.0
                  PortfolioSolver.Constraints.MaxHolding = budget * 0.5 }

        let method =
            match qBackend with
            | :? LocalBackend -> Some HybridSolver.SolverMethod.Classical
            | _ -> Some HybridSolver.SolverMethod.Quantum

        match HybridSolver.solvePortfolio solverAssets constraints None None method with
        | Ok solution ->
            let allocs =
                solution.Result.Allocations
                |> List.map (fun a -> (a.Asset.Symbol, a.Shares, a.Value))

            let sharpe =
                if solution.Result.Risk > 0.0 then solution.Result.ExpectedReturn / solution.Result.Risk
                else 0.0

            Ok {
                Allocations = allocs
                TotalValue = solution.Result.TotalValue
                ExpectedReturn = solution.Result.ExpectedReturn
                Risk = solution.Result.Risk
                SharpeRatio = sharpe
                DiversificationScore = allocs.Length
            }
        | Error e -> Error e.Message

// ==============================================================================
// Main Execution
// ==============================================================================

let quantumBackend = LocalBackend() :> IQuantumBackend

pr "=== Regime-Aware Portfolio Optimization ==="
pr ""
pr "Backend: %s" quantumBackend.Name
pr ""

// 1. Data Generation
pr "Generating %d days of market data (seed %d)..." cliDays cliSeed
let (marketData, trueRegimes, assetHistory) = generateMarketData cliDays cliSeed assets

// 2. Detect Regime
pr "Detecting current market regime (HMM Viterbi)..."
let currentRegime = MarketHMM.detectRegime marketData
let trueRegime = trueRegimes.[cliDays - 1]

pr "  Detected Regime: %A" currentRegime
pr "  True Regime:     %A" trueRegime
if currentRegime = trueRegime then pr "  (Accurate Detection)"
else pr "  (Mismatch - Lag indicators?)"

// 3. Optimize
let strategy = match currentRegime with Bull -> "Maximize Growth (Bull)" | Bear -> "Capital Preservation (Bear)"
pr ""
pr "Optimizing Portfolio..."
pr "  Strategy: %s" strategy
pr "  Budget: $%.0f" cliBudget

match RegimeAwareOptimizer.optimize currentRegime cliBudget quantumBackend assetHistory with
| Ok analysis ->
    pr ""
    pr "Portfolio Allocation:"
    pr "-------------------------------------------"

    analysis.Allocations
    |> List.sortByDescending (fun (_, _, v) -> v)
    |> List.iter (fun (sym, _, value) ->
        let pct = if analysis.TotalValue > 0.0 then (value / analysis.TotalValue) * 100.0 else 0.0
        let name = assets |> List.find (fun a -> a.Symbol = sym) |> fun a -> a.Name
        pr "  %-18s (%-4s): $%-10.2f (%.1f%%)" name sym value pct
    )

    pr ""
    pr "Metrics:"
    pr "-------------------------------------------"
    pr "  Total Invested:  $%.2f" analysis.TotalValue
    pr "  Expected Return: %.4f%%" (analysis.ExpectedReturn * 100.0)
    pr "  Projected Risk:  %.4f%%" (analysis.Risk * 100.0)
    pr "  Sharpe Ratio:    %.2f" analysis.SharpeRatio
    pr "  Diversification: %d assets" analysis.DiversificationScore
    pr ""

    // --- JSON output ---
    outputPath |> Option.iter (fun path ->
        let payload =
            {| detectedRegime = sprintf "%A" currentRegime
               trueRegime = sprintf "%A" trueRegime
               strategy = strategy
               budget = cliBudget
               days = cliDays
               totalInvested = analysis.TotalValue
               expectedReturn = analysis.ExpectedReturn
               risk = analysis.Risk
               sharpeRatio = analysis.SharpeRatio
               diversification = analysis.DiversificationScore
               allocations = analysis.Allocations |> List.map (fun (sym, shares, value) ->
                   {| symbol = sym; shares = shares; value = value |}) |}
        Reporting.writeJson path payload
        pr "JSON written to %s" path
    )

    // --- CSV output ---
    csvPath |> Option.iter (fun path ->
        let header = ["Symbol"; "Name"; "Shares"; "Value"; "Pct"]
        let rows =
            analysis.Allocations
            |> List.sortByDescending (fun (_, _, v) -> v)
            |> List.map (fun (sym, shares, value) ->
                let pct = if analysis.TotalValue > 0.0 then (value / analysis.TotalValue) * 100.0 else 0.0
                let name = assets |> List.find (fun a -> a.Symbol = sym) |> fun a -> a.Name
                [sym; name; sprintf "%.2f" shares; sprintf "%.2f" value; sprintf "%.1f" pct])
        Reporting.writeCsv path header rows
        pr "CSV written to %s" path
    )

| Error e ->
    pr "Optimization Failed: %s" e

// --- Usage hints ---
if not quiet && outputPath.IsNone && csvPath.IsNone then
    pr "-------------------------------------------"
    pr "Tip: Use --budget N to change investment amount (default 100000)."
    pr "     Use --days N to change market data period (default 252)."
    pr "     Use --output results.json or --csv results.csv to export."
    pr "     Use --help for all options."
