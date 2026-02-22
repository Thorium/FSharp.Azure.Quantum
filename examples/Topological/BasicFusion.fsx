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
open FSharp.Azure.Quantum.Core
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
// Quantum backend — topological IQuantumBackend
// ---------------------------------------------------------------------------
let quantumBackend = TopologicalUnifiedBackendFactory.createIsing 10

/// Helper: create a raw 2-sigma-anyon FusionTree.State.
/// This is the minimal "sigma x sigma" system for demonstrating
/// the Ising fusion rule: sigma x sigma = 1 (vacuum) + psi.
let createTwoSigmaState () =
    let tree = FusionTree.fuse
                   (FusionTree.leaf AnyonSpecies.Particle.Sigma)
                   (FusionTree.leaf AnyonSpecies.Particle.Sigma)
                   AnyonSpecies.Particle.Vacuum  // initial channel
    FusionTree.create tree AnyonSpecies.AnyonType.Ising

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

    let state = createTwoSigmaState ()
    let superposition = TopologicalOperations.pureState state

    pr "Initialised 2 sigma anyons"
    pr "  Terms in superposition: %d" superposition.Terms.Length
    for (amp, fusionState) in superposition.Terms do
        pr "  Amplitude: %A   Tree: %A" amp fusionState.Tree

    jsonResults <- ("1_init", box {| anyons = 2; terms = superposition.Terms.Length |}) :: jsonResults
    csvRows <- [ "1_init"; "2"; string superposition.Terms.Length ] :: csvRows

// ---------------------------------------------------------------------------
// Example 2 — Fusion measurement (sigma x sigma = 1 + psi)
// ---------------------------------------------------------------------------
if shouldRun 2 then
    separator ()
    pr "EXAMPLE 2: Measure fusion of two sigma anyons"
    separator ()

    let state = createTwoSigmaState ()
    let result = TopologicalOperations.measureFusion 0 state

    match result with
    | Ok outcomes ->
        // measureFusion returns all possible outcomes with probabilities
        for (probability, opResult) in outcomes do
            match opResult.ClassicalOutcome with
            | Some outcome ->
                let outName =
                    match outcome with
                    | AnyonSpecies.Particle.Vacuum -> "vacuum (trivial)"
                    | AnyonSpecies.Particle.Psi    -> "psi (fermion)"
                    | _                            -> sprintf "%A" outcome
                pr "Outcome: %s   (probability: %.4f)" outName probability

                jsonResults <- ("2_measure", box {| outcome = sprintf "%A" outcome
                                                    probability = probability |}) :: jsonResults
                csvRows <- [ "2_measure"; sprintf "%A" outcome; sprintf "%.4f" probability ] :: csvRows
            | None ->
                pr "Outcome: (no classical outcome)   (probability: %.4f)" probability
    | Error err ->
        pr "Measurement failed: %s" err.Message

// ---------------------------------------------------------------------------
// Example 3 — Fusion statistics
// ---------------------------------------------------------------------------
if shouldRun 3 then
    separator ()
    pr "EXAMPLE 3: Fusion statistics (%d trials)" cliTrials
    separator ()

    // measureFusion on a 2-sigma state returns probabilities deterministically
    // (Born rule from quantum dimensions). Run a single measurement to get the
    // theoretical distribution, then simulate sampling with those weights.
    let state = createTwoSigmaState ()

    match TopologicalOperations.measureFusion 0 state with
    | Ok outcomes ->
        let rng = Random()
        let mutable vacCount = 0
        let mutable psiCount = 0

        // Build cumulative distribution from outcome probabilities
        let cdf =
            outcomes
            |> List.choose (fun (prob, opResult) ->
                opResult.ClassicalOutcome |> Option.map (fun p -> (p, prob)))
            |> List.scan (fun (_, cumProb) (particle, prob) -> (Some particle, cumProb + prob)) (None, 0.0)
            |> List.tail  // drop initial (None, 0.0)

        for _ in 1 .. cliTrials do
            let r = rng.NextDouble ()
            let sampled =
                cdf
                |> List.tryFind (fun (_, cumProb) -> r < cumProb)
                |> Option.bind fst
            match sampled with
            | Some AnyonSpecies.Particle.Vacuum -> vacCount <- vacCount + 1
            | Some AnyonSpecies.Particle.Psi    -> psiCount <- psiCount + 1
            | _                                 -> ()

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
    | Error err ->
        pr "Measurement setup failed: %s" err.Message

// ---------------------------------------------------------------------------
// Example 4 — Four anyons (2-qubit equivalent)
// ---------------------------------------------------------------------------
if shouldRun 4 then
    separator ()
    pr "EXAMPLE 4: Four Ising anyons (2-qubit equivalent)"
    separator ()

    // Use the unified backend: InitializeState 2 creates a 2-qubit
    // topological state encoded in Ising sigma-pairs (+ parity ancilla).
    match quantumBackend.InitializeState 2 with
    | Ok (QuantumState.FusionSuperposition fs) ->
        match TopologicalOperations.fromInterface fs with
        | Some superposition ->
            let numAnyons =
                match superposition.Terms with
                | (_, firstState) :: _ -> FusionTree.leaves firstState.Tree |> List.length
                | [] -> 0
            pr "Initialised 2-qubit state (%d sigma anyons in encoding)" numAnyons
            pr "  Terms in superposition: %d" superposition.Terms.Length
            for (amp, _) in superposition.Terms do
                pr "  Amplitude: %A" amp

            jsonResults <- ("4_four_anyons", box {| qubits = 2; anyons = numAnyons; terms = superposition.Terms.Length |}) :: jsonResults
            csvRows <- [ "4_four_anyons"; string numAnyons; string superposition.Terms.Length ] :: csvRows
        | None ->
            pr "Failed: could not unwrap FusionSuperposition"
    | Ok other ->
        pr "Unexpected state type: %A" other
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
