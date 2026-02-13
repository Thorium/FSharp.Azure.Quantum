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
// Usage:
//   dotnet fsi QuantumVaRStressTest.fsx                              (defaults)
//   dotnet fsi QuantumVaRStressTest.fsx -- --help                    (show options)
//   dotnet fsi QuantumVaRStressTest.fsx -- --confidence 0.95 --shots 500
//   dotnet fsi QuantumVaRStressTest.fsx -- --live --output results.json --csv results.csv
//   dotnet fsi QuantumVaRStressTest.fsx -- --quiet --output results.json
//
// Quantum Advantage:
// Quantum Amplitude Estimation provides a quadratic speedup over classical
// Monte Carlo simulation, allowing for faster convergence or higher precision
// estimates of tail risks.
// ==============================================================================

(*
===============================================================================
 Background Theory
===============================================================================

Value-at-Risk (VaR) is a statistical measure of the potential loss in value of
a portfolio over a defined time horizon for a given confidence level.

This example uses the RiskEngine DSL which internally constructs quantum
circuits for amplitude estimation, providing quadratic speedup over classical
Monte Carlo for tail risk calculations:
  - Classical MC Convergence: Error ~ O(1/sqrt(N))
  - Quantum AE Convergence: Error ~ O(1/N)

References:
  [1] Woerner, S. & Egger, D.J. "Quantum Risk Analysis" npj Quantum Inf 5, 15 (2019)
  [2] Wikipedia: Value_at_risk (https://en.wikipedia.org/wiki/Value_at_risk)
*)

#r "../../src/FSharp.Azure.Quantum/bin/Debug/net10.0/FSharp.Azure.Quantum.dll"

#load "../_common/Cli.fs"
#load "../_common/Data.fs"
#load "../_common/Reporting.fs"

open System
open System.Net.Http
open System.IO
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.Business
open FSharp.Azure.Quantum.Data.FinancialData
open FSharp.Azure.Quantum.Examples.Common

// ==============================================================================
// CLI ARGUMENT PARSING
// ==============================================================================

let argv = fsi.CommandLineArgs |> Array.skip 1
let args = Cli.parse argv

Cli.exitIfHelp
    "QuantumVaRStressTest.fsx"
    "Quantum VaR stress testing using the RiskEngine DSL."
    [ { Cli.OptionSpec.Name = "confidence";        Description = "VaR confidence level (0-1)";            Default = Some "0.99" }
      { Cli.OptionSpec.Name = "shots";             Description = "Quantum circuit shots";                 Default = Some "10000" }
      { Cli.OptionSpec.Name = "qubits";            Description = "Qubits for amplitude estimation";       Default = Some "5" }
      { Cli.OptionSpec.Name = "grover-iterations"; Description = "Grover iterations for amplification";   Default = Some "2" }
      { Cli.OptionSpec.Name = "live";              Description = "Fetch live data from Yahoo Finance";    Default = None }
      { Cli.OptionSpec.Name = "output";            Description = "Write results to JSON file";            Default = None }
      { Cli.OptionSpec.Name = "csv";               Description = "Write results to CSV file";             Default = None }
      { Cli.OptionSpec.Name = "quiet";             Description = "Suppress informational output";         Default = None } ]
    args

let quiet = Cli.hasFlag "quiet" args
let outputPath = Cli.tryGet "output" args
let csvPath = Cli.tryGet "csv" args

// ==============================================================================
// CONFIGURATION
// ==============================================================================

let confidenceLevel = Cli.getFloatOr "confidence" 0.99 args
let shots = Cli.getIntOr "shots" 10_000 args
let numQubits = Cli.getIntOr "qubits" 5 args
let groverIterations = Cli.getIntOr "grover-iterations" 2 args

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
            if not quiet then
                printfn "[DATA] Live Yahoo Finance enabled"
                printfn "[DATA] Cache: %s" yahooCacheDir
                printfn "[DATA] Generated returns CSV: %s" generatedCsvPath
            Some generatedCsvPath
        else
            if not quiet then
                printfn "[DATA] Live Yahoo Finance enabled, but fetch failed"
                printfn "[DATA] Falling back to CSV if present, else mock returns"
                printfn "[DATA] CSV path: %s" defaultCsvPath
            Some defaultCsvPath
    else
        if not quiet then
            printfn "[DATA] Live Yahoo Finance disabled"
            printfn "[DATA] Using CSV if present, else mock returns"
            printfn "[DATA] CSV path: %s" defaultCsvPath
        Some defaultCsvPath

// ==============================================================================
// RISK ENGINE EXECUTION
// ==============================================================================

if not quiet then
    printfn ""
    printfn "╔══════════════════════════════════════════════════════════════╗"
    printfn "║            Quantum VaR Stress Test Engine                    ║"
    printfn "╚══════════════════════════════════════════════════════════════╝"
    printfn ""
    printfn "Configuring Risk Engine..."
    printfn "  Confidence Level:    %.1f%%" (confidenceLevel * 100.0)
    printfn "  Shots:               %d" shots
    printfn "  Qubits:              %d" numQubits
    printfn "  Grover Iterations:   %d" groverIterations
    printfn ""

let riskReport =
    RiskEngine.execute {
        MarketDataPath = marketDataPath
        ConfidenceLevel = confidenceLevel
        SimulationPaths = 1_000_000
        UseAmplitudeEstimation = true
        UseErrorMitigation = true
        Metrics = [ RiskMetric.ValueAtRisk; RiskMetric.ConditionalVaR; RiskMetric.ExpectedShortfall ]
        NumQubits = numQubits
        GroverIterations = groverIterations
        Shots = shots
        Backend = None
        CancellationToken = None
    }

// ==============================================================================
// DISPLAY RESULTS
// ==============================================================================

let formatMetricValue (value: float voption) =
    match value with
    | ValueSome v -> sprintf "%.2f%%" (v * 100.0)
    | ValueNone -> "N/A"

if not quiet then
    printfn "Risk Analysis Report"
    printfn "───────────────────────"
    printfn "Method:           %s" riskReport.Method
    printfn "Confidence Level: %.1f%%" (riskReport.ConfidenceLevel * 100.0)
    printfn "Execution Time:   %.2f ms" riskReport.ExecutionTimeMs
    printfn ""
    printfn "Risk Metrics:"
    printfn "-------------"
    printfn "  %-20s: %s" "Value at Risk (VaR)" (formatMetricValue riskReport.VaR)
    printfn "  %-20s: %s" "Conditional VaR" (formatMetricValue riskReport.CVaR)
    printfn "  %-20s: %s" "Expected Shortfall" (formatMetricValue riskReport.ExpectedShortfall)
    printfn ""
    printfn "Interpretation:"
    printfn "  At %.1f%% confidence, the maximum expected loss is %s of portfolio value."
        (riskReport.ConfidenceLevel * 100.0)
        (formatMetricValue riskReport.VaR)
    printfn "  If that threshold is breached, the average loss (CVaR) is expected to be %s."
        (formatMetricValue riskReport.CVaR)

// ==============================================================================
// STRUCTURED OUTPUT (JSON / CSV)
// ==============================================================================

let metricToString (v: float voption) =
    match v with
    | ValueSome x -> sprintf "%.6f" x
    | ValueNone -> ""

let resultRows =
    [ [ "method", riskReport.Method
        "confidence", sprintf "%.4f" riskReport.ConfidenceLevel
        "execution_time_ms", sprintf "%.2f" riskReport.ExecutionTimeMs
        "var", metricToString riskReport.VaR
        "cvar", metricToString riskReport.CVaR
        "expected_shortfall", metricToString riskReport.ExpectedShortfall
        "qubits", sprintf "%d" numQubits
        "grover_iterations", sprintf "%d" groverIterations
        "shots", sprintf "%d" shots ]
      |> Map.ofList ]

match outputPath with
| Some path ->
    Reporting.writeJson path resultRows
    if not quiet then printfn "Results written to %s" path
| None -> ()

match csvPath with
| Some path ->
    let header = [ "method"; "confidence"; "execution_time_ms"; "var"; "cvar"; "expected_shortfall"; "qubits"; "grover_iterations"; "shots" ]
    let rows =
        resultRows |> List.map (fun m ->
            header |> List.map (fun h -> m |> Map.tryFind h |> Option.defaultValue ""))
    Reporting.writeCsv path header rows
    if not quiet then printfn "Results written to %s" path
| None -> ()

// ==============================================================================
// USAGE HINTS
// ==============================================================================

if not quiet && outputPath.IsNone && csvPath.IsNone && argv.Length = 0 then
    printfn ""
    printfn "Hint: Customize this run with CLI options:"
    printfn "  dotnet fsi QuantumVaRStressTest.fsx -- --confidence 0.95 --shots 500"
    printfn "  dotnet fsi QuantumVaRStressTest.fsx -- --live --output results.json"
    printfn "  dotnet fsi QuantumVaRStressTest.fsx -- --quiet --output results.json --csv results.csv"
    printfn "  dotnet fsi QuantumVaRStressTest.fsx -- --help"
