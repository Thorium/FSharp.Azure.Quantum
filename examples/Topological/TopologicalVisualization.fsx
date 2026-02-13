(*
    Topological Quantum Computing — Visualization
    ================================================

    Visualises fusion trees and quantum superpositions of Ising
    and Fibonacci anyons in ASCII and Mermaid formats.

    Run with: dotnet fsi TopologicalVisualization.fsx
              dotnet fsi TopologicalVisualization.fsx -- --example 3
              dotnet fsi TopologicalVisualization.fsx -- --quiet --output r.json
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

Cli.exitIfHelp "TopologicalVisualization.fsx" "Fusion tree and superposition visualization (Ising & Fibonacci)"
    [ { Name = "example"; Description = "Which example: 1-5|all"; Default = Some "all" }
      { Name = "output";  Description = "Write results to JSON file"; Default = None }
      { Name = "csv";     Description = "Write results to CSV file";  Default = None }
      { Name = "quiet";   Description = "Suppress console output";    Default = None } ] args

let quiet      = Cli.hasFlag "quiet" args
let outputPath = Cli.tryGet "output" args
let csvPath    = Cli.tryGet "csv" args
let exChoice   = Cli.getOr "example" "all" args

let pr fmt = Printf.ksprintf (fun s -> if not quiet then printfn "%s" s) fmt
let shouldRun ex = exChoice = "all" || exChoice = string ex
let separator () = pr "%s" (String.replicate 60 "-")

// ---------------------------------------------------------------------------
// Quantum backend (Rule 1) — topological backend via IQuantumBackend
// ---------------------------------------------------------------------------
let quantumBackend = TopologicalUnifiedBackendFactory.createIsing 10

// Shared anyon particles
let sigma = FusionTree.leaf AnyonSpecies.Particle.Sigma

// Results accumulators
let mutable jsonResults : (string * obj) list = []
let mutable csvRows     : string list list    = []

// ---------------------------------------------------------------------------
// Example 1 — Topological qubit encoding
// ---------------------------------------------------------------------------
if shouldRun 1 then
    separator ()
    pr "EXAMPLE 1: Topological qubit encoding"
    separator ()

    let qubitZero =
        FusionTree.fuse sigma sigma AnyonSpecies.Particle.Vacuum
        |> fun tree -> FusionTree.create tree AnyonSpecies.AnyonType.Ising
    let qubitOne =
        FusionTree.fuse sigma sigma AnyonSpecies.Particle.Psi
        |> fun tree -> FusionTree.create tree AnyonSpecies.AnyonType.Ising

    pr "Qubit |0> (sigma x sigma -> vacuum):"
    pr "%s" (qubitZero.ToASCII())
    pr "Qubit |1> (sigma x sigma -> psi):"
    pr "%s" (qubitOne.ToASCII())

    jsonResults <- ("1_qubit_encoding", box {| state0 = qubitZero.ToASCII()
                                               state1 = qubitOne.ToASCII() |}) :: jsonResults
    csvRows <- [ "1_qubit_encoding"; "sigma x sigma -> vacuum"; "sigma x sigma -> psi" ] :: csvRows

// ---------------------------------------------------------------------------
// Example 2 — Four sigma anyons fusion tree
// ---------------------------------------------------------------------------
if shouldRun 2 then
    separator ()
    pr "EXAMPLE 2: Four sigma anyons fusion tree"
    separator ()

    let leftPair  = FusionTree.fuse sigma sigma AnyonSpecies.Particle.Vacuum
    let rightPair = FusionTree.fuse sigma sigma AnyonSpecies.Particle.Vacuum
    let fourTree  =
        FusionTree.fuse leftPair rightPair AnyonSpecies.Particle.Vacuum
        |> fun tree -> FusionTree.create tree AnyonSpecies.AnyonType.Ising

    pr "%s" (fourTree.ToASCII())
    pr ""
    pr "Mermaid diagram:"
    pr "%s" (fourTree.ToMermaid())

    jsonResults <- ("2_four_sigma", box {| ascii = fourTree.ToASCII()
                                           mermaid = fourTree.ToMermaid() |}) :: jsonResults
    csvRows <- [ "2_four_sigma"; "4 anyons"; string (fourTree.ToASCII().Length) + " chars" ] :: csvRows

// ---------------------------------------------------------------------------
// Example 3 — Quantum superposition
// ---------------------------------------------------------------------------
if shouldRun 3 then
    separator ()
    pr "EXAMPLE 3: Quantum superposition (|0> + |1>) / sqrt(2)"
    separator ()

    let q0 = FusionTree.fuse sigma sigma AnyonSpecies.Particle.Vacuum
              |> fun t -> FusionTree.create t AnyonSpecies.AnyonType.Ising
    let q1 = FusionTree.fuse sigma sigma AnyonSpecies.Particle.Psi
              |> fun t -> FusionTree.create t AnyonSpecies.AnyonType.Ising
    let bellState = TopologicalOperations.uniform [q0; q1] AnyonSpecies.AnyonType.Ising

    pr "Superposition terms:"
    for (amp, state) in bellState.Terms do
        pr "  Amplitude: %A" amp
        pr "  %s" (state.ToASCII())

    jsonResults <- ("3_superposition", box {| terms = bellState.Terms.Length |}) :: jsonResults
    csvRows <- [ "3_superposition"; string bellState.Terms.Length; "uniform" ] :: csvRows

// ---------------------------------------------------------------------------
// Example 4 — Fibonacci anyons
// ---------------------------------------------------------------------------
if shouldRun 4 then
    separator ()
    pr "EXAMPLE 4: Fibonacci anyon fusion tree (tau x tau -> tau)"
    separator ()

    let tau = FusionTree.leaf AnyonSpecies.Particle.Tau
    let fibTree =
        FusionTree.fuse tau tau AnyonSpecies.Particle.Tau
        |> fun t -> FusionTree.create t AnyonSpecies.AnyonType.Fibonacci

    pr "%s" (fibTree.ToASCII())
    pr ""
    pr "Mermaid diagram:"
    pr "%s" (fibTree.ToMermaid())

    jsonResults <- ("4_fibonacci", box {| ascii = fibTree.ToASCII()
                                          mermaid = fibTree.ToMermaid() |}) :: jsonResults
    csvRows <- [ "4_fibonacci"; "tau x tau -> tau"; "Fibonacci" ] :: csvRows

// ---------------------------------------------------------------------------
// Example 5 — Superposition after braiding
// ---------------------------------------------------------------------------
if shouldRun 5 then
    separator ()
    pr "EXAMPLE 5: Superposition after braiding"
    separator ()

    let q0 = FusionTree.fuse sigma sigma AnyonSpecies.Particle.Vacuum
              |> fun t -> FusionTree.create t AnyonSpecies.AnyonType.Ising
    let initialState = TopologicalOperations.pureState q0

    match TopologicalOperations.braidSuperposition 0 initialState with
    | Ok braidedState ->
        pr "After braiding anyon 0 and 1:"
        for (amp, state) in braidedState.Terms do
            pr "  Amplitude: %A" amp
            pr "  %s" (state.ToASCII())

        jsonResults <- ("5_braided", box {| terms = braidedState.Terms.Length
                                            status = "ok" |}) :: jsonResults
        csvRows <- [ "5_braided"; string braidedState.Terms.Length; "success" ] :: csvRows
    | Error err ->
        pr "Braiding error: %s" err.Message
        jsonResults <- ("5_braided", box {| status = "error"; message = err.Message |}) :: jsonResults

// ---------------------------------------------------------------------------
// Output
// ---------------------------------------------------------------------------
if outputPath.IsSome then
    let payload =
        {| script    = "TopologicalVisualization.fsx"
           backend   = "Topological (Ising)"
           timestamp = DateTime.UtcNow.ToString("o")
           example   = exChoice
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
    pr "  dotnet fsi TopologicalVisualization.fsx -- --example 4"
    pr "  dotnet fsi TopologicalVisualization.fsx -- --quiet --output r.json"
    pr "  dotnet fsi TopologicalVisualization.fsx -- --help"
