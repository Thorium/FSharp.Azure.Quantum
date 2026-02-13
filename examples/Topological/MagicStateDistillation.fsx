(*
    Magic State Distillation — Topological Quantum Computing
    ==========================================================

    Demonstrates achieving universal quantum computation with
    Ising anyons using magic state distillation for T-gates.

    Ising anyons (Majorana zero modes) perform only Clifford
    operations natively. Distillation enables non-Clifford T-gates.

    Run with: dotnet fsi MagicStateDistillation.fsx
              dotnet fsi MagicStateDistillation.fsx -- --example 3
              dotnet fsi MagicStateDistillation.fsx -- --error-rate 0.08
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

Cli.exitIfHelp "MagicStateDistillation.fsx" "Magic state distillation for T-gate universality"
    [ { Name = "example";    Description = "Which example: 1-4|all";          Default = Some "all" }
      { Name = "error-rate"; Description = "Initial noisy state error rate";  Default = Some "0.05" }
      { Name = "output";     Description = "Write results to JSON file";      Default = None }
      { Name = "csv";        Description = "Write results to CSV file";       Default = None }
      { Name = "quiet";      Description = "Suppress console output";         Default = None } ] args

let quiet      = Cli.hasFlag "quiet" args
let outputPath = Cli.tryGet "output" args
let csvPath    = Cli.tryGet "csv" args
let exChoice   = Cli.getOr "example" "all" args
let cliError   = Cli.getFloatOr "error-rate" 0.05 args

let pr fmt = Printf.ksprintf (fun s -> if not quiet then printfn "%s" s) fmt
let shouldRun ex = exChoice = "all" || exChoice = string ex
let separator () = pr "%s" (String.replicate 60 "-")
let fmt (x: float) = sprintf "%.6f" x

// ---------------------------------------------------------------------------
// Quantum backend (Rule 1) — topological IQuantumBackend
// ---------------------------------------------------------------------------
let quantumBackend = TopologicalUnifiedBackendFactory.createIsing 10

let random = Random()

// Results accumulators
let mutable jsonResults : (string * obj) list = []
let mutable csvRows     : string list list    = []

// ---------------------------------------------------------------------------
// Example 1 — Single round 15-to-1 distillation
// ---------------------------------------------------------------------------
if shouldRun 1 then
    separator ()
    pr "EXAMPLE 1: Single round 15-to-1 distillation (error=%.1f%%)" (cliError * 100.0)
    separator ()

    let noisyStates =
        [1..15]
        |> List.map (fun _ ->
            MagicStateDistillation.prepareNoisyMagicState cliError AnyonSpecies.AnyonType.Ising)
        |> List.choose (function Ok s -> Some s | Error _ -> None)

    match noisyStates with
    | states when states.Length = 15 ->
        let avgFid = List.averageBy (fun (s: MagicStateDistillation.MagicState) -> s.Fidelity) states
        let avgErr = List.averageBy (fun (s: MagicStateDistillation.MagicState) -> s.ErrorRate) states
        pr "Input: 15 noisy states, avg fidelity %.6f (%.4f%% error)" avgFid (avgErr * 100.0)

        match MagicStateDistillation.distill15to1 random states with
        | Ok distR ->
            let p = distR.PurifiedState
            let suppression = avgErr / p.ErrorRate
            pr "Output fidelity:    %s (%.6f%% error)" (fmt p.Fidelity) (p.ErrorRate * 100.0)
            pr "Error suppression:  %.1fx" suppression
            pr "Acceptance prob:    %s" (fmt distR.AcceptanceProbability)

            jsonResults <- ("1_single_round", box {| inputError = cliError
                                                     outputFidelity = p.Fidelity
                                                     outputError = p.ErrorRate
                                                     suppression = suppression |}) :: jsonResults
            csvRows <- [ "1_single_round"; fmt p.Fidelity; sprintf "%.8f" p.ErrorRate;
                          sprintf "%.1f" suppression ] :: csvRows
        | Error err ->
            pr "Distillation failed: %s" err.Message
    | states ->
        pr "Insufficient states (%d/15)" states.Length

// ---------------------------------------------------------------------------
// Example 2 — Iterative distillation (2 rounds)
// ---------------------------------------------------------------------------
if shouldRun 2 then
    separator ()
    pr "EXAMPLE 2: Two rounds iterative distillation (error=10%%)"
    separator ()

    let initErr = 0.10
    let needed  = 225

    let states =
        [1..needed]
        |> List.map (fun _ ->
            MagicStateDistillation.prepareNoisyMagicState initErr AnyonSpecies.AnyonType.Ising)
        |> List.choose (function Ok s -> Some s | Error _ -> None)

    match states with
    | s when s.Length = needed ->
        pr "Prepared %d noisy states (%.1f%% error)" needed (initErr * 100.0)

        match MagicStateDistillation.distillIterative random 2 s with
        | Ok finalState ->
            let suppression = initErr / finalState.ErrorRate
            pr "Output error:       %.8f (%.6f%%)" finalState.ErrorRate (finalState.ErrorRate * 100.0)
            pr "Total suppression:  %.1fx" suppression
            let theoretical = 35.0 * 35.0 * (initErr ** 9.0)
            pr "Theoretical p_out:  %.8f  (35^2 * p^9)" theoretical

            jsonResults <- ("2_iterative", box {| inputError = initErr
                                                  rounds = 2
                                                  outputError = finalState.ErrorRate
                                                  theoretical = theoretical |}) :: jsonResults
            csvRows <- [ "2_iterative"; sprintf "%.8f" finalState.ErrorRate;
                          sprintf "%.8f" theoretical; sprintf "%.1f" suppression ] :: csvRows
        | Error err ->
            pr "Iterative distillation failed: %s" err.Message
    | s ->
        pr "Insufficient states (%d/%d)" s.Length needed

// ---------------------------------------------------------------------------
// Example 3 — Resource estimation
// ---------------------------------------------------------------------------
if shouldRun 3 then
    separator ()
    pr "EXAMPLE 3: Resource estimation"
    separator ()

    let targetFid = 0.9999
    let noisyFid  = 1.0 - cliError
    pr "Target fidelity: %.2f%%   Noisy fidelity: %.2f%%" (targetFid * 100.0) (noisyFid * 100.0)
    pr ""

    let estimate = MagicStateDistillation.estimateResources targetFid noisyFid
    pr "%s" (MagicStateDistillation.displayResourceEstimate estimate)

    jsonResults <- ("3_resources", box {| targetFidelity = targetFid
                                          noisyFidelity = noisyFid |}) :: jsonResults
    csvRows <- [ "3_resources"; sprintf "%.4f" targetFid; sprintf "%.4f" noisyFid ] :: csvRows

// ---------------------------------------------------------------------------
// Example 4 — Apply T-gate via magic state injection
// ---------------------------------------------------------------------------
if shouldRun 4 then
    separator ()
    pr "EXAMPLE 4: Apply T-gate to topological qubit"
    separator ()

    let sigma' = AnyonSpecies.Particle.Sigma
    let vacuum' = AnyonSpecies.Particle.Vacuum

    let dataQubit =
        let tree =
            FusionTree.fuse
                (FusionTree.fuse
                    (FusionTree.fuse
                        (FusionTree.leaf sigma')
                        (FusionTree.leaf sigma')
                        vacuum')
                    (FusionTree.leaf sigma')
                    vacuum')
                (FusionTree.leaf sigma')
                vacuum'
        FusionTree.create tree AnyonSpecies.AnyonType.Ising

    pr "Data qubit: |0> (4 sigma anyons)"

    let magicStates =
        [1..15]
        |> List.map (fun _ ->
            MagicStateDistillation.prepareNoisyMagicState cliError AnyonSpecies.AnyonType.Ising)
        |> List.choose (function Ok s -> Some s | Error _ -> None)

    match magicStates with
    | ms when ms.Length = 15 ->
        match MagicStateDistillation.distill15to1 random ms with
        | Ok distR ->
            pr "Purified magic state fidelity: %s" (fmt distR.PurifiedState.Fidelity)

            match MagicStateDistillation.applyTGate random dataQubit distR.PurifiedState with
            | Ok tGateR ->
                pr "T-gate applied — gate fidelity: %s" (fmt tGateR.GateFidelity)
                pr ""
                pr "Clifford + T-gate = universal quantum computation!"

                jsonResults <- ("4_t_gate", box {| gateFidelity = tGateR.GateFidelity
                                                   magicFidelity = distR.PurifiedState.Fidelity |}) :: jsonResults
                csvRows <- [ "4_t_gate"; fmt tGateR.GateFidelity;
                              fmt distR.PurifiedState.Fidelity ] :: csvRows
            | Error err ->
                pr "T-gate failed: %s" err.Message
        | Error err ->
            pr "Distillation failed: %s" err.Message
    | ms ->
        pr "Insufficient magic states (%d/15)" ms.Length

// ---------------------------------------------------------------------------
// Output
// ---------------------------------------------------------------------------
if outputPath.IsSome then
    let payload =
        {| script    = "MagicStateDistillation.fsx"
           backend   = "Topological (Ising)"
           timestamp = DateTime.UtcNow.ToString("o")
           example   = exChoice
           errorRate = cliError
           results   = jsonResults |> List.rev |> List.map (fun (k,v) -> {| key = k; value = v |}) |}
    Reporting.writeJson outputPath.Value payload

if csvPath.IsSome then
    let header = [ "example"; "metric1"; "metric2"; "metric3" ]
    Reporting.writeCsv csvPath.Value header (csvRows |> List.rev)

// ---------------------------------------------------------------------------
// Usage hints
// ---------------------------------------------------------------------------
if not quiet && argv.Length = 0 then
    pr ""
    pr "Usage hints:"
    pr "  dotnet fsi MagicStateDistillation.fsx -- --example 3"
    pr "  dotnet fsi MagicStateDistillation.fsx -- --error-rate 0.08"
    pr "  dotnet fsi MagicStateDistillation.fsx -- --quiet --output r.json --csv r.csv"
    pr "  dotnet fsi MagicStateDistillation.fsx -- --help"
