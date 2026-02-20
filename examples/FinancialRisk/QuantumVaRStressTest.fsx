// ==============================================================================
// Quantum VaR Stress Test — RiskEngine DSL
// ==============================================================================
// Compares Value-at-Risk, Conditional VaR, and Expected Shortfall across
// multiple confidence levels using the high-level QuantumRiskEngine DSL.
// The RiskEngine internally uses quantum amplitude estimation for tail risk
// calculations when a quantum backend is provided.
//
// Usage:
//   dotnet fsi QuantumVaRStressTest.fsx                              (defaults)
//   dotnet fsi QuantumVaRStressTest.fsx -- --help                    (show options)
//   dotnet fsi QuantumVaRStressTest.fsx -- --levels 95,99            (select levels)
//   dotnet fsi QuantumVaRStressTest.fsx -- --input custom-levels.csv
//   dotnet fsi QuantumVaRStressTest.fsx -- --quiet --output results.json --csv out.csv
//
// References:
//   [1] Woerner & Egger, "Quantum Risk Analysis" npj Quantum Inf 5, 15 (2019)
//   [2] https://en.wikipedia.org/wiki/Value_at_risk
// ==============================================================================

#r "../../src/FSharp.Azure.Quantum/bin/Debug/net10.0/FSharp.Azure.Quantum.dll"

#load "../_common/Cli.fs"
#load "../_common/Data.fs"
#load "../_common/Reporting.fs"

open System
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Backends.LocalBackend
open FSharp.Azure.Quantum.Business
open FSharp.Azure.Quantum.Examples.Common

// ==============================================================================
// CLI ARGUMENT PARSING
// ==============================================================================

let argv = fsi.CommandLineArgs |> Array.skip 1
let args = Cli.parse argv

Cli.exitIfHelp
    "QuantumVaRStressTest.fsx"
    "Quantum VaR stress testing across confidence levels using the RiskEngine DSL."
    [ { Cli.OptionSpec.Name = "levels";            Description = "Comma-separated confidence levels (%) to include"; Default = None }
      { Cli.OptionSpec.Name = "input";             Description = "CSV file with custom confidence level definitions";  Default = None }
      { Cli.OptionSpec.Name = "shots";             Description = "Quantum circuit shots";                             Default = Some "10000" }
      { Cli.OptionSpec.Name = "qubits";            Description = "Qubits for amplitude estimation";                   Default = Some "5" }
      { Cli.OptionSpec.Name = "grover-iterations"; Description = "Grover iterations for amplification";               Default = Some "2" }
      { Cli.OptionSpec.Name = "paths";             Description = "Monte Carlo simulation paths";                      Default = Some "1000000" }
      { Cli.OptionSpec.Name = "output";            Description = "Write results to JSON file";                        Default = None }
      { Cli.OptionSpec.Name = "csv";               Description = "Write results to CSV file";                         Default = None }
      { Cli.OptionSpec.Name = "quiet";             Description = "Suppress informational output";                     Default = None } ]
    args

let quiet = Cli.hasFlag "quiet" args
let outputPath = Cli.tryGet "output" args
let csvPath = Cli.tryGet "csv" args

// ==============================================================================
// DOMAIN TYPES
// ==============================================================================

/// A confidence level configuration for risk analysis
type LevelInfo = {
    Key: string
    Label: string
    Confidence: float
}

/// Result of risk engine execution at a specific confidence level
type LevelResult = {
    Level: LevelInfo
    Method: string
    VaR: float option
    CVaR: float option
    ExpectedShortfall: float option
    Volatility: float option
    ExecutionTimeMs: float
    HasQuantumFailure: bool
}

// ==============================================================================
// CONFIGURATION
// ==============================================================================

let shots = Cli.getIntOr "shots" 10_000 args
let numQubits = Cli.getIntOr "qubits" 5 args
let groverIterations = Cli.getIntOr "grover-iterations" 2 args
let simulationPaths = Cli.getIntOr "paths" 1_000_000 args

// ==============================================================================
// BUILT-IN CONFIDENCE LEVEL PRESETS
// ==============================================================================

let private preset90  = { Key = "90";   Label = "90% Confidence";   Confidence = 0.90 }
let private preset95  = { Key = "95";   Label = "95% Confidence";   Confidence = 0.95 }
let private preset99  = { Key = "99";   Label = "99% Confidence";   Confidence = 0.99 }
let private preset995 = { Key = "99.5"; Label = "99.5% Confidence"; Confidence = 0.995 }

let private builtInLevels =
    [ preset90; preset95; preset99; preset995 ]
    |> List.map (fun l -> l.Key.ToLowerInvariant(), l)
    |> Map.ofList

// ==============================================================================
// CSV LOADING
// ==============================================================================

let private loadLevelsFromCsv (filePath: string) : LevelInfo list =
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
            match builtInLevels |> Map.tryFind (p.Trim().ToLowerInvariant()) with
            | Some l -> l
            | None -> failwithf "Unknown preset '%s' in CSV row %d" p (i + 1)
        | _ ->
            let confStr = get "confidence"
            let conf =
                match Double.TryParse confStr with
                | true, v when v > 1.0 -> v / 100.0   // Accept percentages like 95 or 99.5
                | true, v -> v
                | _ -> failwithf "Invalid confidence '%s' in CSV row %d" confStr (i + 1)
            let key = get "key" |> fun k -> if k = "" then sprintf "%.1f" (conf * 100.0) else k
            let label = get "label" |> fun l -> if l = "" then sprintf "%.1f%% Confidence" (conf * 100.0) else l
            { Key = key; Label = label; Confidence = conf })

// ==============================================================================
// LEVEL SELECTION
// ==============================================================================

let selectedLevels =
    let base' =
        match Cli.tryGet "input" args with
        | Some csvFile -> loadLevelsFromCsv csvFile
        | None -> builtInLevels |> Map.toList |> List.map snd

    match Cli.getCommaSeparated "levels" args with
    | [] -> base'
    | filter ->
        let filterSet = filter |> List.map (fun s -> s.Trim()) |> Set.ofList
        base' |> List.filter (fun l -> filterSet.Contains l.Key)

if selectedLevels.IsEmpty then
    eprintfn "ERROR: No confidence levels selected. Check --levels filter or --input CSV."
    exit 1

// Sort by confidence ascending for natural presentation
let levels = selectedLevels |> List.sortBy (fun l -> l.Confidence)

// ==============================================================================
// QUANTUM BACKEND (RULE 1)
// ==============================================================================

let backend = LocalBackend() :> IQuantumBackend

// ==============================================================================
// RISK ENGINE EXECUTION
// ==============================================================================

if not quiet then
    printfn "Quantum VaR Stress Test — RiskEngine DSL"
    printfn "Levels: %d  Qubits: %d  Shots: %d  Paths: %s"
        levels.Length numQubits shots (simulationPaths.ToString("N0"))
    printfn ""

let mutable anyFailure = false

let results =
    levels
    |> List.map (fun level ->
        if not quiet then
            printfn "  Running %.1f%% confidence..." (level.Confidence * 100.0)
        try
            let report =
                RiskEngine.execute {
                    MarketDataPath = None
                    ConfidenceLevel = level.Confidence
                    SimulationPaths = simulationPaths
                    UseAmplitudeEstimation = true
                    UseErrorMitigation = true
                    Metrics = [ RiskMetric.ValueAtRisk; RiskMetric.ConditionalVaR; RiskMetric.ExpectedShortfall; RiskMetric.Volatility ]
                    NumQubits = numQubits
                    GroverIterations = groverIterations
                    Shots = shots
                    Backend = Some backend
                    CancellationToken = None
                }
            let toOption (v: float voption) =
                match v with ValueSome x -> Some x | ValueNone -> None
            { Level = level
              Method = report.Method
              VaR = toOption report.VaR
              CVaR = toOption report.CVaR
              ExpectedShortfall = toOption report.ExpectedShortfall
              Volatility = toOption report.Volatility
              ExecutionTimeMs = report.ExecutionTimeMs
              HasQuantumFailure = false }
        with ex ->
            anyFailure <- true
            if not quiet then
                eprintfn "  FAILED at %.1f%%: %s" (level.Confidence * 100.0) ex.Message
            { Level = level
              Method = "Error"
              VaR = None
              CVaR = None
              ExpectedShortfall = None
              Volatility = None
              ExecutionTimeMs = 0.0
              HasQuantumFailure = true })

if not quiet then printfn ""

// ==============================================================================
// COMPARISON TABLE (unconditional)
// ==============================================================================

let private fmtPct (v: float option) =
    match v with
    | Some x -> sprintf "%8.4f%%" (x * 100.0)
    | None   -> sprintf "%9s" "—"

let private fmtMs (ms: float) = sprintf "%8.1f" ms

let printTable () =
    let divider = String('-', 96)
    printfn ""
    printfn "  VaR Stress Test — Risk Metrics by Confidence Level"
    printfn "  %s" divider
    printfn "  %-20s %9s %9s %9s %9s %8s %8s %8s"
        "Level" "VaR" "CVaR" "ES" "Vol" "Time(ms)" "Method" "Status"
    printfn "  %s" divider
    for r in results do
        let methodShort =
            if r.Method.Contains("Quantum") then "QAE"
            elif r.Method.Contains("Classical") then "CMC"
            else r.Method
        let status = if r.HasQuantumFailure then "FAIL" else "OK"
        printfn "  %-20s %s %s %s %s %s %8s %8s"
            r.Level.Label
            (fmtPct r.VaR)
            (fmtPct r.CVaR)
            (fmtPct r.ExpectedShortfall)
            (fmtPct r.Volatility)
            (fmtMs r.ExecutionTimeMs)
            methodShort
            status
    printfn "  %s" divider
    printfn ""

printTable ()

// ==============================================================================
// STRUCTURED OUTPUT (JSON / CSV)
// ==============================================================================

let private metricStr (v: float option) =
    match v with Some x -> sprintf "%.6f" x | None -> ""

let resultMaps : Map<string, string> list =
    results
    |> List.map (fun r ->
        [ "key",                    r.Level.Key
          "label",                  r.Level.Label
          "confidence",             sprintf "%.4f" r.Level.Confidence
          "method",                 r.Method
          "var",                    metricStr r.VaR
          "cvar",                   metricStr r.CVaR
          "expected_shortfall",     metricStr r.ExpectedShortfall
          "volatility",             metricStr r.Volatility
          "execution_time_ms",      sprintf "%.2f" r.ExecutionTimeMs
          "qubits",                 sprintf "%d" numQubits
          "grover_iterations",      sprintf "%d" groverIterations
          "shots",                  sprintf "%d" shots
          "simulation_paths",       sprintf "%d" simulationPaths
          "has_quantum_failure",    sprintf "%b" r.HasQuantumFailure ]
        |> Map.ofList)

match outputPath with
| Some path ->
    Reporting.writeJson path resultMaps
    if not quiet then printfn "Results written to %s" path
| None -> ()

match csvPath with
| Some path ->
    let header =
        [ "key"; "label"; "confidence"; "method"; "var"; "cvar"; "expected_shortfall"
          "volatility"; "execution_time_ms"; "qubits"; "grover_iterations"; "shots"
          "simulation_paths"; "has_quantum_failure" ]
    let rows =
        resultMaps |> List.map (fun m ->
            header |> List.map (fun h -> m |> Map.tryFind h |> Option.defaultValue ""))
    Reporting.writeCsv path header rows
    if not quiet then printfn "Results written to %s" path
| None -> ()
