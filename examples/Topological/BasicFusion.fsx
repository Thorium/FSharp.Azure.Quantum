(*
    Basic Fusion Example — Topological Quantum Computing
    ======================================================

    Demonstrates fundamental fusion rules of Ising anyons:
      sigma x sigma = 1 (vacuum) + psi (fermion)

    These are the building blocks of Microsoft's topological quantum
    computer using Majorana zero modes.

    Run with: dotnet fsi BasicFusion.fsx
              dotnet fsi BasicFusion.fsx -- --example 3 --trials 500
              dotnet fsi BasicFusion.fsx -- --quiet --output r.json --csv r.csv
*)

#r "../../src/FSharp.Azure.Quantum/bin/Debug/net10.0/FSharp.Azure.Quantum.dll"
#r "../../src/FSharp.Azure.Quantum.Topological/bin/Debug/net10.0/FSharp.Azure.Quantum.Topological.dll"
#load "../_common/Cli.fs"
#load "../_common/Data.fs"
#load "../_common/Reporting.fs"

open System
open FSharp.Azure.Quantum.Topological
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Examples.Common

// ---------------------------------------------------------------------------
// CLI
// ---------------------------------------------------------------------------
let argv = fsi.CommandLineArgs |> Array.skip 1
let args = Cli.parse argv

Cli.exitIfHelp "BasicFusion.fsx" "Ising anyon fusion rules and measurement statistics"
    [ { Name = "example"; Description = "Which example: 1-4|all"; Default = Some "all" }
      { Name = "trials";  Description = "Fusion statistics trials"; Default = Some "1000" }
      { Name = "output";  Description = "Write results to JSON file"; Default = None }
      { Name = "csv";     Description = "Write results to CSV file";  Default = None }
      { Name = "quiet";   Description = "Suppress console output";    Default = None } ] args

let quiet      = Cli.hasFlag "quiet" args
let outputPath = Cli.tryGet "output" args
let csvPath    = Cli.tryGet "csv" args
let exChoice   = Cli.getOr "example" "all" args
let cliTrials  = Cli.getIntOr "trials" 1000 args

let pr fmt = Printf.ksprintf (fun s -> if not quiet then printfn "%s" s) fmt
let shouldRun ex = exChoice = "all" || exChoice = string ex
let separator () = pr "%s" (String.replicate 60 "-")

// ---------------------------------------------------------------------------
// Quantum backend (Rule 1) — topological IQuantumBackend
// ---------------------------------------------------------------------------
let quantumBackend = TopologicalUnifiedBackendFactory.createIsing 10

// Legacy topological backend for fusion-specific operations
let topoBackend = TopologicalBackend.createSimulator AnyonSpecies.AnyonType.Ising 10

// Results accumulators
let mutable jsonResults : (string * obj) list = []
let mutable csvRows     : string list list    = []

// ---------------------------------------------------------------------------
// Example 1 — Initialise Ising anyons
// ---------------------------------------------------------------------------
if shouldRun 1 then
    separator ()
    pr "EXAMPLE 1: Initialise 2 Ising anyons (sigma particles)"
    separator ()

    let state2 =
        task { return! topoBackend.Initialize AnyonSpecies.AnyonType.Ising 2 }
        |> Async.AwaitTask |> Async.RunSynchronously

    match state2 with
    | Ok state ->
        pr "Initialised 2 sigma anyons"
        pr "  Terms in superposition: %d" state.Terms.Length
        for (amp, fusionState) in state.Terms do
            pr "  Amplitude: %A   Tree: %A" amp fusionState.Tree

        jsonResults <- ("1_init", box {| anyons = 2; terms = state.Terms.Length |}) :: jsonResults
        csvRows <- [ "1_init"; "2"; string state.Terms.Length ] :: csvRows
    | Error err ->
        pr "Initialisation failed: %s" err.Message

// ---------------------------------------------------------------------------
// Example 2 — Fusion measurement (sigma x sigma = 1 + psi)
// ---------------------------------------------------------------------------
if shouldRun 2 then
    separator ()
    pr "EXAMPLE 2: Measure fusion of two sigma anyons"
    separator ()

    let result =
        task {
            let! initR = topoBackend.Initialize AnyonSpecies.AnyonType.Ising 2
            match initR with
            | Ok state ->
                let! measR = topoBackend.MeasureFusion 0 state
                return measR
            | Error e -> return Error e
        } |> Async.AwaitTask |> Async.RunSynchronously

    match result with
    | Ok (outcome, collapsed, probability) ->
        let outName =
            match outcome with
            | AnyonSpecies.Particle.Vacuum -> "vacuum (trivial)"
            | AnyonSpecies.Particle.Psi    -> "psi (fermion)"
            | _                            -> sprintf "%A" outcome
        pr "Outcome: %s   (probability: %.4f)" outName probability
        pr "Collapsed state terms: %d" collapsed.Terms.Length

        jsonResults <- ("2_measure", box {| outcome = sprintf "%A" outcome
                                            probability = probability |}) :: jsonResults
        csvRows <- [ "2_measure"; sprintf "%A" outcome; sprintf "%.4f" probability ] :: csvRows
    | Error err ->
        pr "Measurement failed: %s" err.Message

// ---------------------------------------------------------------------------
// Example 3 — Fusion statistics
// ---------------------------------------------------------------------------
if shouldRun 3 then
    separator ()
    pr "EXAMPLE 3: Fusion statistics (%d trials)" cliTrials
    separator ()

    let (vacCount, psiCount) =
        task {
            let mutable vac = 0
            let mutable psi = 0
            for _ in 1 .. cliTrials do
                let! initR = topoBackend.Initialize AnyonSpecies.AnyonType.Ising 2
                match initR with
                | Ok state ->
                    let! measR = topoBackend.MeasureFusion 0 state
                    match measR with
                    | Ok (AnyonSpecies.Particle.Vacuum, _, _) -> vac <- vac + 1
                    | Ok (AnyonSpecies.Particle.Psi, _, _)    -> psi <- psi + 1
                    | _ -> ()
                | _ -> ()
            return (vac, psi)
        } |> Async.AwaitTask |> Async.RunSynchronously

    let vacPct = float vacCount / float cliTrials * 100.0
    let psiPct = float psiCount / float cliTrials * 100.0
    pr "Vacuum (1): %d times (%.1f%%)" vacCount vacPct
    pr "Psi    (ψ): %d times (%.1f%%)" psiCount psiPct
    pr ""
    pr "Expected: ~50%% vacuum, ~50%% psi  (from sigma x sigma = 1 + psi)"

    jsonResults <- ("3_stats", box {| trials = cliTrials; vacuum = vacCount; psi = psiCount
                                      vacuumPct = vacPct; psiPct = psiPct |}) :: jsonResults
    csvRows <- [ "3_stats"; string vacCount; string psiCount;
                  sprintf "%.1f" vacPct; sprintf "%.1f" psiPct ] :: csvRows

// ---------------------------------------------------------------------------
// Example 4 — Four anyons (2-qubit equivalent)
// ---------------------------------------------------------------------------
if shouldRun 4 then
    separator ()
    pr "EXAMPLE 4: Four Ising anyons (2-qubit equivalent)"
    separator ()

    let result =
        task { return! topoBackend.Initialize AnyonSpecies.AnyonType.Ising 4 }
        |> Async.AwaitTask |> Async.RunSynchronously

    match result with
    | Ok state ->
        pr "Initialised 4 anyons (2-dimensional fusion space)"
        pr "  Terms in superposition: %d" state.Terms.Length
        for (amp, _) in state.Terms do
            pr "  Amplitude: %A" amp

        jsonResults <- ("4_four_anyons", box {| anyons = 4; terms = state.Terms.Length |}) :: jsonResults
        csvRows <- [ "4_four_anyons"; "4"; string state.Terms.Length ] :: csvRows
    | Error err ->
        pr "Failed: %s" err.Message

// ---------------------------------------------------------------------------
// Output
// ---------------------------------------------------------------------------
if outputPath.IsSome then
    let payload =
        {| script    = "BasicFusion.fsx"
           backend   = "Topological (Ising)"
           timestamp = DateTime.UtcNow.ToString("o")
           example   = exChoice
           trials    = cliTrials
           results   = jsonResults |> List.rev |> List.map (fun (k,v) -> {| key = k; value = v |}) |}
    Reporting.writeJson outputPath.Value payload

if csvPath.IsSome then
    let header = [ "example"; "detail1"; "detail2"; "detail3"; "detail4" ]
    Reporting.writeCsv csvPath.Value header (csvRows |> List.rev)

// ---------------------------------------------------------------------------
// Usage hints
// ---------------------------------------------------------------------------
if not quiet && argv.Length = 0 then
    pr ""
    pr "Usage hints:"
    pr "  dotnet fsi BasicFusion.fsx -- --example 3 --trials 500"
    pr "  dotnet fsi BasicFusion.fsx -- --quiet --output r.json --csv r.csv"
    pr "  dotnet fsi BasicFusion.fsx -- --help"
