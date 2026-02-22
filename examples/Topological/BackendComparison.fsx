(*
    Backend Comparison -- Ising vs Fibonacci Anyons
    =================================================

    Compares topological backend configurations: capabilities,
    fusion rules, performance, fusion statistics, and validation.

    Examples:
      1  Backend capabilities (Ising & Fibonacci)
      2  Fusion rule summary
      3  Performance comparison (initialize + braid)
      4  Computational power table
      5  Fusion measurement statistics
      6  Capability validation

    Run with: dotnet fsi BackendComparison.fsx
              dotnet fsi BackendComparison.fsx -- --example 5 --trials 500
              dotnet fsi BackendComparison.fsx -- --quiet --output r.json --csv r.csv
*)

#r "nuget: Microsoft.Extensions.Logging.Abstractions, 10.0.0"
#r "../../src/FSharp.Azure.Quantum/bin/Debug/net10.0/FSharp.Azure.Quantum.dll"
#r "../../src/FSharp.Azure.Quantum.Topological/bin/Debug/net10.0/FSharp.Azure.Quantum.Topological.dll"
#load "../_common/Cli.fs"
#load "../_common/Data.fs"
#load "../_common/Reporting.fs"

open System
open System.Diagnostics
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Topological
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Examples.Common

// ---------------------------------------------------------------------------
// CLI
// ---------------------------------------------------------------------------
let argv = fsi.CommandLineArgs |> Array.skip 1
let args = Cli.parse argv

Cli.exitIfHelp "BackendComparison.fsx" "Compare Ising vs Fibonacci topological backends"
    [ { Name = "example"; Description = "Which example: 1-6|all"; Default = Some "all" }
      { Name = "trials";  Description = "Fusion statistics trials"; Default = Some "1000" }
      { Name = "output";  Description = "Write results to JSON file"; Default = None }
      { Name = "csv";     Description = "Write results to CSV file";  Default = None }
      { Name = "quiet";   Description = "Suppress console output";    Default = None } ] args

let quiet      = Cli.hasFlag "quiet" args
let outputPath = Cli.tryGet "output" args
let csvPath    = Cli.tryGet "csv" args
let exChoice   = Cli.getOr "example" "all" args
let numTrials  = Cli.getIntOr "trials" 1000 args

let pr fmt = Printf.ksprintf (fun s -> if not quiet then printfn "%s" s) fmt
let shouldRun ex = exChoice = "all" || exChoice = string ex
let separator () = pr "%s" (String.replicate 60 "-")

// ---------------------------------------------------------------------------
// Backends (Rule 1 -- IQuantumBackend unified API)
// ---------------------------------------------------------------------------
let quantumBackend  = TopologicalUnifiedBackendFactory.createIsing 20
let fibUnified      = TopologicalUnifiedBackendFactory.createFibonacci 20

// Results accumulators
let mutable jsonResults : (string * obj) list = []
let mutable csvRows     : string list list    = []

// ---------------------------------------------------------------------------
// Example 1 -- Backend capabilities
// ---------------------------------------------------------------------------
if shouldRun 1 then
    separator ()
    pr "EXAMPLE 1: Backend Capabilities"
    separator ()

    let showCaps label (b: IQuantumBackend) =
        pr "  %s:" label
        pr "    Name:             %s" b.Name
        pr "    Braiding:         %b" (b.SupportsOperation (QuantumOperation.Braid 0))
        pr "    Measurement:      %b" (b.SupportsOperation (QuantumOperation.Measure 0))
        pr "    F-Moves:          %b" (b.SupportsOperation (QuantumOperation.FMove (FMoveDirection.Forward, 1)))
        pr "    H gate:           %b" (b.SupportsOperation (QuantumOperation.Gate (CircuitBuilder.H 0)))
        pr "    CNOT gate:        %b" (b.SupportsOperation (QuantumOperation.Gate (CircuitBuilder.CNOT (0, 1))))

    showCaps "Ising (Microsoft Majorana)" quantumBackend
    pr ""
    showCaps "Fibonacci (Theoretical Universal)" fibUnified

    jsonResults <- ("1_capabilities", box {| ising = "ok"; fibonacci = "ok" |}) :: jsonResults
    csvRows <- [ "1_capabilities"; "ok"; "ok" ] :: csvRows

// ---------------------------------------------------------------------------
// Example 2 -- Fusion rules
// ---------------------------------------------------------------------------
if shouldRun 2 then
    separator ()
    pr "EXAMPLE 2: Fusion Rules"
    separator ()

    pr "  Ising {1, sigma, psi}:"
    pr "    sigma x sigma = 1 + psi  (superposition)"
    pr "    sigma x psi   = sigma    (fermion ~ Z gate)"
    pr "    psi   x psi   = 1        (annihilation)"
    pr ""
    pr "  Fibonacci {1, tau}:"
    pr "    tau x tau = 1 + tau  (golden ratio superposition)"
    pr "    (Simpler particles, MORE powerful!)"

    jsonResults <- ("2_fusion_rules", box {| summary = "ok" |}) :: jsonResults
    csvRows <- [ "2_fusion_rules"; "ok" ] :: csvRows

// ---------------------------------------------------------------------------
// Example 3 -- Performance comparison
// ---------------------------------------------------------------------------
if shouldRun 3 then
    separator ()
    pr "EXAMPLE 3: Performance Comparison (Init + 3 Braids)"
    separator ()

    let bench (qb: IQuantumBackend) anyonType label = task {
        let sw = Stopwatch.StartNew()
        let program = topological qb {
            do! TopologicalBuilder.initialize anyonType 6
            do! TopologicalBuilder.braid 0
            do! TopologicalBuilder.braid 2
            do! TopologicalBuilder.braid 4
        }
        let! result = TopologicalBuilder.execute qb program
        sw.Stop()
        match result with
        | Ok () ->
            pr "  %s: %.3f ms" label sw.Elapsed.TotalMilliseconds
            return Ok sw.Elapsed.TotalMilliseconds
        | Error err ->
            pr "  %s: FAILED - %s" label err.Message
            return Error err
    }

    let iT = bench quantumBackend AnyonSpecies.AnyonType.Ising "Ising    "
             |> Async.AwaitTask |> Async.RunSynchronously
    let fT = bench fibUnified AnyonSpecies.AnyonType.Fibonacci "Fibonacci"
             |> Async.AwaitTask |> Async.RunSynchronously

    match iT, fT with
    | Ok t1, Ok t2 ->
        if t1 < t2 then pr "  Ising %.2fx faster" (t2 / t1)
        else pr "  Fibonacci %.2fx faster" (t1 / t2)

        jsonResults <- ("3_performance", box {| ising_ms = t1; fib_ms = t2 |}) :: jsonResults
        csvRows <- [ "3_performance"; sprintf "%.3f" t1; sprintf "%.3f" t2 ] :: csvRows
    | _ ->
        pr "  Comparison incomplete"

// ---------------------------------------------------------------------------
// Example 4 -- Computational power table
// ---------------------------------------------------------------------------
if shouldRun 4 then
    separator ()
    pr "EXAMPLE 4: Computational Power"
    separator ()

    pr "  %-18s %-17s %-20s" "Capability" "Ising" "Fibonacci"
    pr "  %s" (String.replicate 56 "-")
    pr "  %-18s %-17s %-20s" "Clifford Gates"   "Yes"           "Yes"
    pr "  %-18s %-17s %-20s" "T Gate"            "Magic States"  "Braiding Only"
    pr "  %-18s %-17s %-20s" "Universal QC"      "Hybrid"        "Pure Braiding"
    pr "  %-18s %-17s %-20s" "Hardware Status"   "Experimental"  "Theoretical"
    pr "  %-18s %-17s %-20s" "Fusion Outcomes"   "3 particles"   "2 particles"

    jsonResults <- ("4_power", box {| summary = "ok" |}) :: jsonResults
    csvRows <- [ "4_power"; "ok" ] :: csvRows

// ---------------------------------------------------------------------------
// Example 5 -- Fusion statistics
// ---------------------------------------------------------------------------
if shouldRun 5 then
    separator ()
    pr "EXAMPLE 5: Fusion Measurement Statistics (%d trials)" numTrials
    separator ()

    let runStats (qb: IQuantumBackend) label =
        let outcomes = System.Collections.Generic.Dictionary<string, int>()
        for _ in 1 .. numTrials do
            match qb.InitializeState 1 with
            | Ok state ->
                match qb.ApplyOperation (QuantumOperation.Measure 0) state with
                | Ok (QuantumState.FusionSuperposition _) ->
                    let key = "measured"
                    if outcomes.ContainsKey(key) then outcomes.[key] <- outcomes.[key] + 1
                    else outcomes.[key] <- 1
                | Ok _ ->
                    let key = "other"
                    if outcomes.ContainsKey(key) then outcomes.[key] <- outcomes.[key] + 1
                    else outcomes.[key] <- 1
                | Error _ -> ()
            | Error _ -> ()

        pr "  %s (%d trials):" label numTrials
        let mutable resultPairs : (string * float) list = []
        for kvp in outcomes do
            let pct = (float kvp.Value / float numTrials) * 100.0
            pr "    %s: %d (%.1f%%)" kvp.Key kvp.Value pct
            resultPairs <- (kvp.Key, pct) :: resultPairs
        resultPairs

    let isingStats = runStats quantumBackend "Ising (unified API)"
    let fibStats   = runStats fibUnified "Fibonacci (unified API)"

    jsonResults <- ("5_fusion_stats", box {| trials = numTrials; ising = isingStats; fibonacci = fibStats |}) :: jsonResults
    csvRows <- [ "5_fusion_stats"; string numTrials ] :: csvRows

// ---------------------------------------------------------------------------
// Example 6 -- Operation support validation
// ---------------------------------------------------------------------------
if shouldRun 6 then
    separator ()
    pr "EXAMPLE 6: Operation Support Validation"
    separator ()

    let validate (qb: IQuantumBackend) ops label =
        let allSupported = ops |> List.forall qb.SupportsOperation
        if allSupported then pr "  %s: PASS" label; "PASS"
        else pr "  %s: FAIL" label; "FAIL"

    let r1 = validate quantumBackend
                [ QuantumOperation.Braid 0
                  QuantumOperation.Measure 0
                  QuantumOperation.FMove (FMoveDirection.Forward, 1) ]
                "Ising supports braiding, measure, fmove"

    let r2 = validate quantumBackend
                [ QuantumOperation.Gate (CircuitBuilder.H 0)
                  QuantumOperation.Gate (CircuitBuilder.CNOT (0, 1)) ]
                "Ising supports H and CNOT gates"

    let r3 = validate fibUnified
                [ QuantumOperation.Braid 0
                  QuantumOperation.Measure 0 ]
                "Fibonacci supports braiding and measure"

    jsonResults <- ("6_validation", box {| r1 = r1; r2 = r2; r3 = r3 |}) :: jsonResults
    csvRows <- [ "6_validation"; r1; r2; r3 ] :: csvRows

// ---------------------------------------------------------------------------
// Recommendations
// ---------------------------------------------------------------------------
if not quiet then
    separator ()
    pr "Recommendations:"
    pr "  Ising:     Microsoft Majorana roadmap, Clifford circuits"
    pr "  Fibonacci: Theoretical exploration, universal braiding"
    separator ()

// ---------------------------------------------------------------------------
// Output
// ---------------------------------------------------------------------------
if outputPath.IsSome then
    let payload =
        {| script    = "BackendComparison.fsx"
           backend   = quantumBackend.Name
           timestamp = DateTime.UtcNow.ToString("o")
           example   = exChoice
           trials    = numTrials
           results   = jsonResults |> List.rev |> List.map (fun (k,v) -> {| key = k; value = v |}) |}
    Reporting.writeJson outputPath.Value payload

if csvPath.IsSome then
    let header = [ "example"; "detail1"; "detail2" ]
    Reporting.writeCsv csvPath.Value header (csvRows |> List.rev)

// ---------------------------------------------------------------------------
// Usage hints
// ---------------------------------------------------------------------------
if not quiet && argv.Length = 0 then
    pr ""
    pr "Usage hints:"
    pr "  dotnet fsi BackendComparison.fsx -- --example 5 --trials 500"
    pr "  dotnet fsi BackendComparison.fsx -- --quiet --output r.json --csv r.csv"
    pr "  dotnet fsi BackendComparison.fsx -- --help"
