// ==============================================================================
// Quantum VaR Stress Test
// ==============================================================================
// Demonstrates the use of the QuantumRiskEngine DSL for financial stress testing.
//
// Business Context:
// Banks and asset managers must calculate Value at Risk (VaR) and Conditional 
// VaR (Expected Shortfall) to quantify potential losses in extreme market 
// scenarios.
//
// This example uses the high-level 'quantumRiskEngine' builder to:
// 1. Configure the risk simulation parameters.
// 2. Select quantum acceleration (Amplitude Estimation).
// 3. Calculate key risk metrics (VaR, CVaR).
//
// Quantum Advantage:
// Quantum Amplitude Estimation provides a quadratic speedup over classical 
// Monte Carlo simulation, allowing for faster convergence or higher precision 
// estimates of tail risks.
// ==============================================================================

#r "../../src/FSharp.Azure.Quantum/bin/Debug/net10.0/FSharp.Azure.Quantum.dll"

open System
open System.Net.Http
open System.IO
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.Business
open FSharp.Azure.Quantum.Data.FinancialData

printfn "╔══════════════════════════════════════════════════════════════╗"
printfn "║            Quantum VaR Stress Test Engine                    ║"
printfn "╚══════════════════════════════════════════════════════════════╝"
printfn ""

let liveDataEnabled =
    let liveArgEnabled =
        Environment.GetCommandLineArgs()
        |> Array.exists (fun a ->
            match a.Trim().ToLowerInvariant() with
            | "--live" | "-live" | "/live" | "--yahoo" -> true
            | _ -> false)

    let liveEnvEnabled =
        Environment.GetEnvironmentVariable("FINANCIALRISK_LIVE_DATA")
        |> Option.ofObj
        |> Option.map (fun s -> s.Trim().ToLowerInvariant())
        |> Option.exists (fun s -> s = "1" || s = "true" || s = "yes")

    liveArgEnabled || liveEnvEnabled

let yahooCacheDir = Path.Combine(__SOURCE_DIRECTORY__, "output", "yahoo-cache")
let _ = Directory.CreateDirectory(yahooCacheDir) |> ignore

let writeReturnsCsv (filePath: string) (returns: float[]) =
    let lines =
        Array.append
            [| "return" |]
            (returns |> Array.map (fun r -> r.ToString("R", Globalization.CultureInfo.InvariantCulture)))

    File.WriteAllLines(filePath, lines)

let tryCreateMarketDataCsvFromYahoo (symbols: string list) (outputCsvPath: string) : bool =
    try
        use httpClient = new HttpClient()

        let allDailyReturns =
            symbols
            |> List.collect (fun symbol ->
                let request: YahooHistoryRequest = {
                    Symbol = symbol
                    Range = YahooHistoryRange.TwoYears
                    Interval = YahooHistoryInterval.OneDay
                    IncludeAdjustedClose = true
                    CacheDirectory = Some yahooCacheDir
                    CacheTtl = TimeSpan.FromHours(6.0)
                }

                match fetchYahooHistory httpClient request with
                | Ok priceSeries ->
                    let rs = calculateReturns priceSeries
                    rs.LogReturns |> Array.toList
                | Error _ -> raise (InvalidOperationException(sprintf "Failed to fetch Yahoo data for %s" symbol)))

        if List.isEmpty allDailyReturns then
            false
        else
            writeReturnsCsv outputCsvPath (allDailyReturns |> List.toArray)
            true
    with _ -> false

let defaultCsvPath = "market_data_2023_2024.csv"
let generatedCsvPath = Path.Combine(__SOURCE_DIRECTORY__, "output", "market_data_yahoo_generated.csv")

let marketDataPath =
    if liveDataEnabled then
        let symbols = [ "SPY"; "QQQ"; "TLT"; "GLD"; "VWO" ]
        if tryCreateMarketDataCsvFromYahoo symbols generatedCsvPath then
            printfn "[DATA] Live Yahoo Finance enabled"
            printfn "[DATA] Cache: %s" yahooCacheDir
            printfn "[DATA] Generated returns CSV: %s" generatedCsvPath
            Some generatedCsvPath
        else
            printfn "[DATA] Live Yahoo Finance enabled, but fetch failed"
            printfn "[DATA] Falling back to CSV if present, else mock returns"
            printfn "[DATA] CSV path: %s" defaultCsvPath
            Some defaultCsvPath
    else
        printfn "[DATA] Live Yahoo Finance disabled"
        printfn "[DATA] Using CSV if present, else mock returns"
        printfn "[DATA] CSV path: %s" defaultCsvPath
        Some defaultCsvPath

// Define the stress test scenario using the DSL
printfn "Configuring Risk Engine..."
let riskReport =
    RiskEngine.execute {
        MarketDataPath = marketDataPath
        ConfidenceLevel = 0.99
        SimulationPaths = 1_000_000
        UseAmplitudeEstimation = true
        UseErrorMitigation = true
        Metrics = [ RiskMetric.ValueAtRisk; RiskMetric.ConditionalVaR; RiskMetric.ExpectedShortfall ]
        NumQubits = 5
        GroverIterations = 2
        Shots = 100
        Backend = None
    }

// Report Results
printfn ""
printfn "📊 Risk Analysis Report"
printfn "───────────────────────"
printfn "Method:           %s" riskReport.Method
printfn "Confidence Level: %.1f%%" (riskReport.ConfidenceLevel * 100.0)
printfn "Execution Time:   %.2f ms" riskReport.ExecutionTimeMs
printfn ""
printfn "Risk Metrics:"
printfn "-------------"

let formatMetric name value =
    match value with
    | ValueSome v -> printfn "  %-20s: %.2f%%" name (v * 100.0)
    | ValueNone -> printfn "  %-20s: N/A" name

formatMetric "Value at Risk (VaR)" riskReport.VaR
formatMetric "Conditional VaR" riskReport.CVaR
formatMetric "Expected Shortfall" riskReport.ExpectedShortfall

printfn ""
printfn "Interpretation:"
printfn "  At %.1f%% confidence, the maximum expected loss is %.2f%% of portfolio value." 
    (riskReport.ConfidenceLevel * 100.0) 
    (riskReport.VaR |> ValueOption.defaultValue 0.0 |> fun v -> v * 100.0)
printfn "  If that threshold is breached, the average loss (CVaR) is expected to be %.2f%%."
    (riskReport.CVaR |> ValueOption.defaultValue 0.0 |> fun v -> v * 100.0)
printfn ""
