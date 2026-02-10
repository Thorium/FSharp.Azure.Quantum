/// Quantum-Enhanced Statistical Distributions
///
/// USE CASE: Sample from standard distributions using quantum random number generation
///
/// PROBLEM: Classical PRNGs are deterministic; quantum measurement is fundamentally
/// random. This module uses QRNG to produce samples from Normal, LogNormal,
/// Exponential, Uniform, and custom distributions with true quantum entropy.
///
/// Applications: Monte Carlo simulations, financial modelling, scientific computing.

(*
===============================================================================
 Background Theory
===============================================================================

Quantum Random Number Generation (QRNG) exploits the Born rule: measuring a
qubit in superposition |+> = (|0> + |1>)/sqrt(2) yields 0 or 1 with equal
probability, fundamentally (not computationally) unpredictable. By collecting
multiple quantum bits and applying Inverse Transform Sampling, we convert
uniform quantum randomness into any target distribution F^{-1}(U) where
U ~ Uniform(0,1).

Supported distributions:
  - Normal N(mu, sigma)       via Box-Muller transform
  - LogNormal LN(mu, sigma)   via exp(Normal)
  - Exponential Exp(lambda)   via -ln(1-U)/lambda
  - Uniform U(a, b)           via a + (b-a)*U
  - Custom f(U)               via user-supplied transform

All sampling functions accept an IQuantumBackend, ensuring the entropy
source can be a real quantum device (Rigetti, IonQ, Quantinuum) or a
local simulator — Rule 1 compliant.

References:
  [1] Herrero-Collantes & Garcia-Escartin, "Quantum random number generators",
      Rev. Mod. Phys. 89, 015004 (2017).
  [2] Wikipedia: Quantum random number generator
      https://en.wikipedia.org/wiki/Hardware_random_number_generator#Quantum_random_number_generators

Usage:
  dotnet fsi QuantumDistributions.fsx                              (defaults)
  dotnet fsi QuantumDistributions.fsx -- --help                    (show options)
  dotnet fsi QuantumDistributions.fsx -- --example stock           (stock simulation)
  dotnet fsi QuantumDistributions.fsx -- --samples 200 --example all
  dotnet fsi QuantumDistributions.fsx -- --quiet --output results.json
*)

//#r "nuget: FSharp.Azure.Quantum"
#r "../../src/FSharp.Azure.Quantum/bin/Debug/net10.0/FSharp.Azure.Quantum.dll"

#load "../_common/Cli.fs"
#load "../_common/Data.fs"
#load "../_common/Reporting.fs"

open System
open FSharp.Azure.Quantum.Algorithms
open FSharp.Azure.Quantum.Algorithms.QuantumDistributions
open FSharp.Azure.Quantum.Backends
open FSharp.Azure.Quantum.Examples.Common

// ==============================================================================
// CLI ARGUMENT PARSING
// ==============================================================================

let argv = fsi.CommandLineArgs |> Array.skip 1
let args = Cli.parse argv

Cli.exitIfHelp
    "QuantumDistributions.fsx"
    "Quantum random sampling from statistical distributions."
    [ { Cli.OptionSpec.Name = "example"; Description = "Example: normal|statistics|stock|server|dice|custom|backend|montecarlo|all"; Default = Some "normal" }
      { Cli.OptionSpec.Name = "samples"; Description = "Number of samples per distribution";   Default = Some "100" }
      { Cli.OptionSpec.Name = "output";  Description = "Write results to JSON file";            Default = None }
      { Cli.OptionSpec.Name = "csv";     Description = "Write results to CSV file";              Default = None }
      { Cli.OptionSpec.Name = "quiet";   Description = "Suppress informational output";          Default = None } ]
    args

let quiet = Cli.hasFlag "quiet" args
let outputPath = Cli.tryGet "output" args
let csvPath = Cli.tryGet "csv" args
let exampleName = Cli.getOr "example" "normal" args
let sampleCount = Cli.getIntOr "samples" 100 args

// ==============================================================================
// DISPLAY HELPERS
// ==============================================================================

let printHeader title =
    if not quiet then
        printfn ""
        printfn "%s" title
        printfn "%s" (String.replicate (String.length title) "-")

// ==============================================================================
// RESULT ROW BUILDER
// ==============================================================================

let statsRow (label: string) (dist: string) (stats: SampleStatistics) : Map<string, string> =
    Map.ofList
        [ "example",      label
          "distribution",  dist
          "count",         sprintf "%d" stats.Count
          "mean",          sprintf "%.4f" stats.Mean
          "stddev",        sprintf "%.4f" stats.StdDev
          "min",           sprintf "%.4f" stats.Min
          "max",           sprintf "%.4f" stats.Max ]

let singleRow (label: string) (dist: string) (value: float) (qubits: int) : Map<string, string> =
    Map.ofList
        [ "example",      label
          "distribution",  dist
          "count",         "1"
          "mean",          sprintf "%.4f" value
          "stddev",        ""
          "min",           sprintf "%.4f" value
          "max",           sprintf "%.4f" value ]

// ==============================================================================
// EXAMPLES
// ==============================================================================

let allResults = ResizeArray<Map<string, string>>()

/// Example 1: Basic Normal sampling
let runNormal () =
    printHeader "Example 1: Normal Distribution Sampling"

    let dist = Normal (mean = 100.0, stddev = 15.0)

    match sample StandardNormal with
    | Ok result ->
        if not quiet then
            printfn "  Standard Normal N(0,1) sample: %.4f  (%d qubits)" result.Value result.QuantumBitsUsed
    | Error msg ->
        if not quiet then printfn "  Error: %s" msg

    match sample dist with
    | Ok result ->
        if not quiet then
            printfn "  Normal N(100,15) sample: %.2f" result.Value
            printfn "  Expected mean: %.2f, stddev: %.2f"
                (expectedMean dist |> Option.defaultValue 0.0)
                (expectedStdDev dist |> Option.defaultValue 0.0)
        allResults.Add (singleRow "normal" "N(100,15)" result.Value result.QuantumBitsUsed)
    | Error msg ->
        if not quiet then printfn "  Error: %s" msg

/// Example 2: Multiple samples with statistics
let runStatisticsExample () =
    printHeader "Example 2: Multiple Samples with Statistics"

    let dist = Normal (mean = 50.0, stddev = 10.0)

    match sampleMany dist sampleCount with
    | Ok samples ->
        let stats = computeStatistics samples
        if not quiet then
            printfn "  Distribution: N(50, 10), %d samples" stats.Count
            printfn "  Mean:   %.2f (expected 50.00)" stats.Mean
            printfn "  StdDev: %.2f (expected 10.00)" stats.StdDev
            printfn "  Range:  [%.2f, %.2f]" stats.Min stats.Max
        allResults.Add (statsRow "statistics" "N(50,10)" stats)
    | Error msg ->
        if not quiet then printfn "  Error: %s" msg

/// Example 3: LogNormal stock price simulation
let runStock () =
    printHeader "Example 3: LogNormal Stock Price Simulation"

    let s0 = 100.0
    let mu = 0.05
    let sigma = 0.2
    let t = 1.0

    let logMu = log s0 + (mu - sigma ** 2.0 / 2.0) * t
    let logSigma = sigma * sqrt t
    let dist = LogNormal (mu = logMu, sigma = logSigma)

    if not quiet then
        printfn "  S0=$%.0f, drift=%.0f%%, vol=%.0f%%, T=%.0f yr" s0 (mu * 100.0) (sigma * 100.0) t

    match sampleMany dist (min sampleCount 10) with
    | Ok samples ->
        let stats = computeStatistics samples
        if not quiet then
            printfn "  Simulated %d price paths:" samples.Length
            samples |> Array.iteri (fun i s ->
                let ret = (s.Value - s0) / s0 * 100.0
                printfn "    Path %2d: $%.2f (%+.1f%%)" (i + 1) s.Value ret)
            printfn "  Average final price: $%.2f" stats.Mean
        allResults.Add (statsRow "stock" "LogNormal" stats)
    | Error msg ->
        if not quiet then printfn "  Error: %s" msg

/// Example 4: Exponential for server request arrivals
let runServer () =
    printHeader "Example 4: Exponential Distribution (Server Arrivals)"

    let lambda = 5.0
    let dist = Exponential (lambda = lambda)

    if not quiet then
        printfn "  Rate: %.1f requests/sec, expected interval: %.3fs" lambda (1.0 / lambda)

    match sampleMany dist (min sampleCount 15) with
    | Ok samples ->
        let stats = computeStatistics samples
        if not quiet then
            let mutable cumulative = 0.0
            samples |> Array.iteri (fun i s ->
                cumulative <- cumulative + s.Value
                printfn "    Request %2d: %.3fs (cumulative: %.2fs)" (i + 1) s.Value cumulative)
            printfn "  Mean interval: %.3fs (expected %.3fs)" stats.Mean (1.0 / lambda)
        allResults.Add (statsRow "server" (sprintf "Exp(%.1f)" lambda) stats)
    | Error msg ->
        if not quiet then printfn "  Error: %s" msg

/// Example 5: Uniform distribution (dice)
let runDice () =
    printHeader "Example 5: Uniform Distribution (Dice Rolls)"

    let dist = Uniform (min = 1.0, max = 7.0)

    match sampleMany dist (min sampleCount 20) with
    | Ok samples ->
        let stats = computeStatistics samples
        let rolls = samples |> Array.map (fun s -> int (floor s.Value))
        if not quiet then
            let rollStr = rolls |> Array.map string |> String.concat ", "
            printfn "  %d rolls: %s" rolls.Length rollStr
            let freq = rolls |> Array.countBy id |> Array.sortBy fst
            printfn "  Frequencies:"
            for (v, c) in freq do
                printfn "    %d: %s (%d)" v (String.replicate c "#") c
        allResults.Add (statsRow "dice" "U(1,7)" stats)
    | Error msg ->
        if not quiet then printfn "  Error: %s" msg

/// Example 6: Custom transform
let runCustom () =
    printHeader "Example 6: Custom Distribution (Square Transform)"

    let squareTransform (u: float) = u * u
    let dist = Custom (name = "Square", transform = squareTransform)

    match sampleMany dist sampleCount with
    | Ok samples ->
        let stats = computeStatistics samples
        if not quiet then
            printfn "  Transform U^2 on %d samples" stats.Count
            printfn "  Mean:   %.4f (expected ~0.333)" stats.Mean
            printfn "  StdDev: %.4f" stats.StdDev
            printfn "  Range:  [%.4f, %.4f]" stats.Min stats.Max
        allResults.Add (statsRow "custom" "U^2" stats)
    | Error msg ->
        if not quiet then printfn "  Error: %s" msg

/// Example 7: Backend integration via LocalBackend
let runBackend () =
    printHeader "Example 7: Explicit Backend Integration (LocalBackend)"

    let backend =
        LocalBackend.LocalBackend() :> FSharp.Azure.Quantum.Core.BackendAbstraction.IQuantumBackend
    let dist = StandardNormal

    async {
        match! sampleManyWithBackend dist 5 backend None with
        | Ok samples ->
            let stats = computeStatistics samples
            if not quiet then
                printfn "  Generated %d samples via LocalBackend:" samples.Length
                samples |> Array.iteri (fun i s ->
                    printfn "    Sample %d: %.4f (%d qubits)" (i + 1) s.Value s.QuantumBitsUsed)
            allResults.Add (statsRow "backend" "N(0,1) via LocalBackend" stats)
        | Error err ->
            if not quiet then printfn "  Error: %s" err.Message
    }
    |> Async.RunSynchronously

/// Example 8: Monte Carlo estimation of pi
let runMonteCarlo () =
    printHeader "Example 8: Monte Carlo Estimation of pi"

    let dist = Uniform (min = 0.0, max = 1.0)
    let pairCount = max 50 (sampleCount / 2)

    match sampleMany dist (pairCount * 2) with
    | Ok samples ->
        let points = samples |> Array.chunkBySize 2
        let inside =
            points
            |> Array.filter (fun p ->
                p.Length = 2 && p.[0].Value ** 2.0 + p.[1].Value ** 2.0 <= 1.0)
            |> Array.length
        let total = points.Length
        let piEst = 4.0 * float inside / float total
        let err = abs (piEst - Math.PI)

        if not quiet then
            printfn "  Points: %d, inside quarter-circle: %d" total inside
            printfn "  pi estimate:  %.6f" piEst
            printfn "  true pi:      %.6f" Math.PI
            printfn "  error:        %.6f (%.3f%%)" err (err / Math.PI * 100.0)

        allResults.Add (Map.ofList
            [ "example",      "montecarlo"
              "distribution",  "U(0,1) pairs"
              "count",         sprintf "%d" total
              "mean",          sprintf "%.6f" piEst
              "stddev",        ""
              "min",           sprintf "%.6f" err
              "max",           "" ])
    | Error msg ->
        if not quiet then printfn "  Error: %s" msg

// ==============================================================================
// MAIN EXECUTION
// ==============================================================================

if not quiet then
    printfn "======================================"
    printfn "Quantum-Enhanced Statistical Distributions"
    printfn "======================================"

match exampleName.ToLowerInvariant() with
| "all" ->
    runNormal ()
    runStatisticsExample ()
    runStock ()
    runServer ()
    runDice ()
    runCustom ()
    runBackend ()
    runMonteCarlo ()
| "normal"     -> runNormal ()
| "statistics" -> runStatisticsExample ()
| "stock"      -> runStock ()
| "server"     -> runServer ()
| "dice"       -> runDice ()
| "custom"     -> runCustom ()
| "backend"    -> runBackend ()
| "montecarlo" -> runMonteCarlo ()
| other ->
    eprintfn "Unknown example: '%s'. Use: normal|statistics|stock|server|dice|custom|backend|montecarlo|all" other
    exit 1

if not quiet then
    printfn ""
    printfn "======================================"
    printfn "Quantum Distributions Complete!"
    printfn "======================================"

// ==============================================================================
// STRUCTURED OUTPUT
// ==============================================================================

let resultRows = allResults |> Seq.toList

match outputPath with
| Some path ->
    Reporting.writeJson path resultRows
    if not quiet then printfn "Results written to %s" path
| None -> ()

match csvPath with
| Some path ->
    let header = [ "example"; "distribution"; "count"; "mean"; "stddev"; "min"; "max" ]
    let rows =
        resultRows
        |> List.map (fun m ->
            header |> List.map (fun h -> m |> Map.tryFind h |> Option.defaultValue ""))
    Reporting.writeCsv path header rows
    if not quiet then printfn "Results written to %s" path
| None -> ()

// ==============================================================================
// USAGE HINTS
// ==============================================================================

if argv.Length = 0 && not quiet then
    printfn ""
    printfn "Tip: Run with --help to see all options:"
    printfn "   dotnet fsi QuantumDistributions.fsx -- --help"
    printfn "   dotnet fsi QuantumDistributions.fsx -- --example all"
    printfn "   dotnet fsi QuantumDistributions.fsx -- --samples 200 --example stock"
    printfn "   dotnet fsi QuantumDistributions.fsx -- --quiet --csv results.csv"
    printfn ""
