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

#r "../../src/FSharp.Azure.Quantum/bin/Debug/net10.0/FSharp.Azure.Quantum.dll"
#r "../../src/FSharp.Azure.Quantum.Topological/bin/Debug/net10.0/FSharp.Azure.Quantum.Topological.dll"
#load "../_common/Cli.fs"
#load "../_common/Data.fs"
#load "../_common/Reporting.fs"

open System
open System.Diagnostics
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
// Backends (Rule 1 -- IQuantumBackend + legacy ITopologicalBackend)
// ---------------------------------------------------------------------------
let isingBackend    = TopologicalBackend.createSimulator AnyonSpecies.AnyonType.Ising 10
let fibBackend      = TopologicalBackend.createSimulator AnyonSpecies.AnyonType.Fibonacci 10
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

    let showCaps label (b: TopologicalBackend.ITopologicalBackend) =
        let c = b.Capabilities
        pr "  %s:" label
        pr "    Anyon types:      %A" c.SupportedAnyonTypes
        pr "    Max anyons:       %A" c.MaxAnyons
        pr "    Braiding:         %b" c.SupportsBraiding
        pr "    Measurement:      %b" c.SupportsMeasurement
        pr "    F-Moves:          %b" c.SupportsFMoves
        pr "    Error correction: %b" c.SupportsErrorCorrection

    showCaps "Ising (Microsoft Majorana)" isingBackend
    pr ""
    showCaps "Fibonacci (Theoretical Universal)" fibBackend

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

    let runStats (lb: TopologicalBackend.ITopologicalBackend) anyonType label = task {
        let outcomes = System.Collections.Generic.Dictionary<AnyonSpecies.Particle, int>()
        for _ in 1 .. numTrials do
            let! initR = lb.Initialize anyonType 2
            match initR with
            | Ok st ->
                let! measR = lb.MeasureFusion 0 st
                match measR with
                | Ok (outcome, _, _) ->
                    if outcomes.ContainsKey(outcome) then outcomes.[outcome] <- outcomes.[outcome] + 1
                    else outcomes.[outcome] <- 1
                | Error _ -> ()
            | Error _ -> ()

        pr "  %s (%d trials):" label numTrials
        let mutable resultPairs : (string * float) list = []
        for kvp in outcomes do
            let pct = (float kvp.Value / float numTrials) * 100.0
            pr "    %A: %d (%.1f%%)" kvp.Key kvp.Value pct
            resultPairs <- (sprintf "%A" kvp.Key, pct) :: resultPairs
        return resultPairs
    }

    let isingStats = runStats isingBackend AnyonSpecies.AnyonType.Ising "Ising sigma x sigma"
                     |> Async.AwaitTask |> Async.RunSynchronously
    let fibStats   = runStats fibBackend AnyonSpecies.AnyonType.Fibonacci "Fibonacci tau x tau"
                     |> Async.AwaitTask |> Async.RunSynchronously

    jsonResults <- ("5_fusion_stats", box {| trials = numTrials; ising = isingStats; fibonacci = fibStats |}) :: jsonResults
    csvRows <- [ "5_fusion_stats"; string numTrials ] :: csvRows

// ---------------------------------------------------------------------------
// Example 6 -- Capability validation
// ---------------------------------------------------------------------------
if shouldRun 6 then
    separator ()
    pr "EXAMPLE 6: Capability Validation"
    separator ()

    let validate (lb: TopologicalBackend.ITopologicalBackend) reqs label =
        match TopologicalBackend.validateCapabilities lb reqs with
        | Ok () -> pr "  %s: PASS" label; "PASS"
        | Error err -> pr "  %s: FAIL - %s" label err.Message; "FAIL"

    let isingReqs = {
        TopologicalBackend.SupportedAnyonTypes = [ AnyonSpecies.AnyonType.Ising ]
        TopologicalBackend.MaxAnyons = Some 4
        TopologicalBackend.SupportsBraiding = true
        TopologicalBackend.SupportsMeasurement = true
        TopologicalBackend.SupportsFMoves = false
        TopologicalBackend.SupportsErrorCorrection = false
    }
    let r1 = validate isingBackend isingReqs "Ising meets Ising reqs"

    let wrongReqs = {
        TopologicalBackend.SupportedAnyonTypes = [ AnyonSpecies.AnyonType.Fibonacci ]
        TopologicalBackend.MaxAnyons = None
        TopologicalBackend.SupportsBraiding = true
        TopologicalBackend.SupportsMeasurement = true
        TopologicalBackend.SupportsFMoves = false
        TopologicalBackend.SupportsErrorCorrection = false
    }
    let r2 = validate isingBackend wrongReqs "Ising meets Fibonacci reqs (expect fail)"

    jsonResults <- ("6_validation", box {| correct = r1; wrong = r2 |}) :: jsonResults
    csvRows <- [ "6_validation"; r1; r2 ] :: csvRows

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
