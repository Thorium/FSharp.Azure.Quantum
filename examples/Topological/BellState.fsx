(*
    Bell State Creation — Topological Quantum Computing
    =====================================================

    Creates entangled Bell state via braiding operations instead
    of quantum gates. Braiding creates entanglement geometrically
    and is topologically protected (immune to local noise).

    Run with: dotnet fsi BellState.fsx
              dotnet fsi BellState.fsx -- --example 2 --trials 200
              dotnet fsi BellState.fsx -- --quiet --output r.json --csv r.csv
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

Cli.exitIfHelp "BellState.fsx" "Bell state creation via topological braiding operations"
    [ { Name = "example"; Description = "Which example: 1-3|all";       Default = Some "all" }
      { Name = "trials";  Description = "Correlation test trials";      Default = Some "100" }
      { Name = "output";  Description = "Write results to JSON file";   Default = None }
      { Name = "csv";     Description = "Write results to CSV file";    Default = None }
      { Name = "quiet";   Description = "Suppress console output";      Default = None } ] args

let quiet      = Cli.hasFlag "quiet" args
let outputPath = Cli.tryGet "output" args
let csvPath    = Cli.tryGet "csv" args
let exChoice   = Cli.getOr "example" "all" args
let cliTrials  = Cli.getIntOr "trials" 100 args

let pr fmt = Printf.ksprintf (fun s -> if not quiet then printfn "%s" s) fmt
let shouldRun ex = exChoice = "all" || exChoice = string ex
let separator () = pr "%s" (String.replicate 60 "-")

// ---------------------------------------------------------------------------
// Quantum backend (Rule 1) — topological IQuantumBackend
// ---------------------------------------------------------------------------
let quantumBackend = TopologicalUnifiedBackendFactory.createIsing 10

// Results accumulators
let mutable jsonResults : (string * obj) list = []
let mutable csvRows     : string list list    = []

// ---------------------------------------------------------------------------
// Example 1 — Create Bell state via braiding
// ---------------------------------------------------------------------------
if shouldRun 1 then
    separator ()
    pr "EXAMPLE 1: Create Bell state via topological braiding"
    separator ()

    let bellProgram = topological quantumBackend {
        do! TopologicalBuilder.initialize AnyonSpecies.AnyonType.Ising 4
        do! TopologicalBuilder.braid 0   // braid anyons 0 and 1
        do! TopologicalBuilder.braid 2   // braid anyons 2 and 3
    }

    let result =
        task { return! TopologicalBuilder.execute quantumBackend bellProgram }
        |> Async.AwaitTask |> Async.RunSynchronously

    match result with
    | Ok () ->
        pr "Bell state created via braiding"
        pr "  1. Initialise 4 sigma anyons"
        pr "  2. Braid anyon 0 around anyon 1"
        pr "  3. Braid anyon 2 around anyon 3"
        pr "  Result: entangled topological state"

        jsonResults <- ("1_bell_state", box {| status = "ok"; anyons = 4
                                               braids = [| 0; 2 |] |}) :: jsonResults
        csvRows <- [ "1_bell_state"; "ok"; "4 anyons"; "braids 0,2" ] :: csvRows
    | Error err ->
        pr "Failed: %s" err.Message

// ---------------------------------------------------------------------------
// Example 2 — Entanglement correlation test
// ---------------------------------------------------------------------------
if shouldRun 2 then
    separator ()
    pr "EXAMPLE 2: Entanglement correlation test (%d trials)" cliTrials
    separator ()

    let correlatedCount =
        task {
            let mutable corr = 0
            for _ in 1 .. cliTrials do
                let trialProgram = topological quantumBackend {
                    do! TopologicalBuilder.initialize AnyonSpecies.AnyonType.Ising 4
                    do! TopologicalBuilder.braid 0
                    do! TopologicalBuilder.braid 2
                    let! o1 = TopologicalBuilder.measure 0
                    let! o2 = TopologicalBuilder.measure 0
                    return (o1, o2)
                }
                let! r = TopologicalBuilder.execute quantumBackend trialProgram
                match r with
                | Ok (o1, o2) ->
                    let same =
                        (o1 = AnyonSpecies.Particle.Vacuum && o2 = AnyonSpecies.Particle.Vacuum) ||
                        (o1 = AnyonSpecies.Particle.Psi    && o2 = AnyonSpecies.Particle.Psi)
                    if same then corr <- corr + 1
                | Error _ -> ()
            return corr
        } |> Async.AwaitTask |> Async.RunSynchronously

    let corrPct = float correlatedCount / float cliTrials * 100.0
    pr "Correlated:   %d (%.1f%%)" correlatedCount corrPct
    pr "Uncorrelated: %d (%.1f%%)" (cliTrials - correlatedCount) (100.0 - corrPct)
    pr ""
    if corrPct > 75.0 then
        pr "Strong correlation — entanglement verified"
    else
        pr "Correlation weaker than expected"

    jsonResults <- ("2_correlation", box {| trials = cliTrials
                                            correlated = correlatedCount
                                            correlationPct = corrPct |}) :: jsonResults
    csvRows <- [ "2_correlation"; string cliTrials; string correlatedCount;
                  sprintf "%.1f" corrPct ] :: csvRows

// ---------------------------------------------------------------------------
// Example 3 — Gate-based vs topological comparison
// ---------------------------------------------------------------------------
if shouldRun 3 then
    separator ()
    pr "EXAMPLE 3: Gate-based vs topological comparison"
    separator ()
    pr "Gate-based:                          Topological:"
    pr "  Initial: |00>                        Initial: sigma sigma sigma sigma"
    pr "  H(q0) -> superposition               Braid(0) -> geometric entanglement"
    pr "  CNOT(0,1) -> entangle                Braid(2) -> correlate"
    pr "  Result: (|00>+|11>)/sqrt(2)          Result: entangled fusion tree"
    pr ""
    pr "Advantage: topological protection (immune to local perturbations)"
    pr ""
    pr "Braiding worldline:"
    pr "  Time"
    pr "    |  s  s  s  s     (4 anyons at t=0)"
    pr "    |  |\\ /|  |  |"
    pr "    |  | X |  |  |    Braid(0)"
    pr "    |  |/ \\|  |  |"
    pr "    |  |  |  |\\ /|"
    pr "    |  |  |  | X |    Braid(2)"
    pr "    |  |  |  |/ \\|"
    pr "    v                  (entangled)"

    jsonResults <- ("3_comparison", box {| gateOps = "H, CNOT"
                                           topoOps = "Braid(0), Braid(2)" |}) :: jsonResults
    csvRows <- [ "3_comparison"; "H+CNOT"; "Braid(0)+Braid(2)" ] :: csvRows

// ---------------------------------------------------------------------------
// Output
// ---------------------------------------------------------------------------
if outputPath.IsSome then
    let payload =
        {| script    = "BellState.fsx"
           backend   = "Topological (Ising)"
           timestamp = DateTime.UtcNow.ToString("o")
           example   = exChoice
           trials    = cliTrials
           results   = jsonResults |> List.rev |> List.map (fun (k,v) -> {| key = k; value = v |}) |}
    Reporting.writeJson outputPath.Value payload

if csvPath.IsSome then
    let header = [ "example"; "detail1"; "detail2"; "detail3" ]
    Reporting.writeCsv csvPath.Value header (csvRows |> List.rev)

// ---------------------------------------------------------------------------
// Usage hints
// ---------------------------------------------------------------------------
if not quiet && argv.Length = 0 then
    pr ""
    pr "Usage hints:"
    pr "  dotnet fsi BellState.fsx -- --example 2 --trials 200"
    pr "  dotnet fsi BellState.fsx -- --quiet --output r.json --csv r.csv"
    pr "  dotnet fsi BellState.fsx -- --help"
