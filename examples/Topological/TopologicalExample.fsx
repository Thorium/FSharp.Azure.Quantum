(*
    Topological Quantum Computing -- Layered Architecture Demo
    ============================================================

    Demonstrates the full topological computing stack through 6 layers:
      1  Core math: fusion rules, quantum dimensions
      2  Backend capabilities: IQuantumBackend properties
      3  Quantum circuit: initialize, braid, measure via IQuantumBackend
      4  Algorithm: knot invariant via braiding measurement
      5  Builder pattern: topological { } computation expression
      6  Business application: topological error detection

    Run with: dotnet fsi TopologicalExample.fsx
              dotnet fsi TopologicalExample.fsx -- --example 3
              dotnet fsi TopologicalExample.fsx -- --quiet --output r.json --csv r.csv
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

Cli.exitIfHelp "TopologicalExample.fsx" "Layered topological quantum computing architecture demo"
    [ { Name = "example"; Description = "Which example: 1-6|all"; Default = Some "all" }
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
// Quantum backend (Rule 1)
// ---------------------------------------------------------------------------
let quantumBackend = TopologicalUnifiedBackendFactory.createIsing 20

// Results accumulators
let mutable jsonResults : (string * obj) list = []
let mutable csvRows     : string list list    = []

// Fold a sequence of braid operations over a quantum state, short-circuiting on error
let applyBraids indices state =
    (Ok state, indices) ||> List.fold (fun acc idx ->
        acc |> Result.bind (fun s ->
            quantumBackend.ApplyOperation (QuantumOperation.Braid idx) s
            |> Result.mapError (fun e -> sprintf "Braid %d failed: %A" idx e)))

// ---------------------------------------------------------------------------
// Example 1 -- Core Topological Math (Layer 1)
// ---------------------------------------------------------------------------
if shouldRun 1 then
    separator ()
    pr "EXAMPLE 1: Core Fusion Rules (Layer 1 - Math)"
    separator ()

    let channels =
        FusionRules.channels
            AnyonSpecies.Particle.Sigma
            AnyonSpecies.Particle.Sigma
            AnyonSpecies.AnyonType.Ising

    match channels with
    | Ok ch ->
        pr "  sigma x sigma fusion channels:"
        ch |> List.iter (fun c -> pr "    %A" c)
    | Error err ->
        pr "  Error: %s" err.Message

    let sigmaDim = AnyonSpecies.quantumDimension AnyonSpecies.Particle.Sigma
    pr "  Quantum dimension of sigma: %.4f" sigmaDim

    jsonResults <- ("1_core", box {| sigmaDim = sigmaDim |}) :: jsonResults
    csvRows <- [ "1_core"; sprintf "%.4f" sigmaDim ] :: csvRows

// ---------------------------------------------------------------------------
// Example 2 -- Backend Capabilities (Layer 2)
// ---------------------------------------------------------------------------
if shouldRun 2 then
    separator ()
    pr "EXAMPLE 2: Backend Capabilities (Layer 2)"
    separator ()

    pr "  Name:            %s" quantumBackend.Name
    pr "  Native state:    %A" quantumBackend.NativeStateType
    pr "  Supports braid:  %b" (quantumBackend.SupportsOperation (QuantumOperation.Braid 0))
    pr "  Supports measure: %b" (quantumBackend.SupportsOperation (QuantumOperation.Measure 0))

    jsonResults <- ("2_backend", box {| name = quantumBackend.Name |}) :: jsonResults
    csvRows <- [ "2_backend"; quantumBackend.Name ] :: csvRows

// ---------------------------------------------------------------------------
// Example 3 -- Quantum Circuit (Layer 3)
// ---------------------------------------------------------------------------
if shouldRun 3 then
    separator ()
    pr "EXAMPLE 3: Topological Quantum Circuit (Layer 3)"
    separator ()

    let circuitResult =
        pr "  Initializing 4-anyon qubit..."
        quantumBackend.InitializeState 4
        |> Result.mapError (fun e -> sprintf "Init failed: %A" e)
        |> Result.bind (fun qubit ->
            match qubit with
            | QuantumState.FusionSuperposition fs ->
                pr "  Initial state: %d logical qubits" fs.LogicalQubits
            | _ -> pr "  State created (abstract)"

            pr "  Applying braiding sequence..."
            applyBraids [ 0; 2; 0 ] qubit)
        |> Result.bind (fun state ->
            match state with
            | QuantumState.FusionSuperposition fs ->
                pr "  After braiding: %d logical qubits" fs.LogicalQubits
            | _ -> ()

            pr "  Measuring fusion..."
            match state with
            | QuantumState.FusionSuperposition fs ->
                match TopologicalOperations.fromInterface fs with
                | Some nativeState ->
                    let singleState = snd (List.head nativeState.Terms)
                    TopologicalOperations.measureFusion 0 singleState
                    |> Result.mapError (fun e -> sprintf "Measure: %s" e.Message)
                    |> Result.bind (fun outcomes ->
                        let (prob, result) = List.head outcomes
                        match result.ClassicalOutcome with
                        | Some outcome ->
                            pr "  Outcome: %A (prob: %.4f)" outcome prob
                            Ok (sprintf "%A" outcome, prob)
                        | None -> Ok ("collapsed", 0.0))
                | None -> Error "Could not unwrap state"
            | _ -> Error "Invalid state type")

    match circuitResult with
    | Ok (outcome, prob) ->
        jsonResults <- ("3_circuit", box {| outcome = outcome; probability = prob |}) :: jsonResults
        csvRows <- [ "3_circuit"; outcome; sprintf "%.4f" prob ] :: csvRows
    | Error msg ->
        pr "  Error: %s" msg
        jsonResults <- ("3_circuit", box {| error = msg |}) :: jsonResults
        csvRows <- [ "3_circuit"; "error"; msg ] :: csvRows

// ---------------------------------------------------------------------------
// Example 4 -- Knot Invariant Algorithm (Layer 4)
// ---------------------------------------------------------------------------
if shouldRun 4 then
    separator ()
    pr "EXAMPLE 4: Knot Invariant via Braiding (Layer 4)"
    separator ()

    let braidingPattern = [ 0; 2; 0; 2; 0; 2 ]

    let knotResult =
        pr "  Braiding pattern (trefoil): %A" braidingPattern
        quantumBackend.InitializeState 6
        |> Result.mapError (fun e -> sprintf "Init: %A" e)
        |> Result.bind (applyBraids braidingPattern)
        |> Result.bind (fun state ->
            match state with
            | QuantumState.FusionSuperposition fs ->
                match TopologicalOperations.fromInterface fs with
                | Some nativeState ->
                    let singleState = snd (List.head nativeState.Terms)
                    TopologicalOperations.measureFusion 0 singleState
                    |> Result.mapError (fun e -> e.Message)
                    |> Result.bind (fun outcomes ->
                        let (prob, result) = List.head outcomes
                        match result.ClassicalOutcome with
                        | Some outcome ->
                            pr "  Fusion outcome: %A" outcome
                            pr "  Reannihilation probability: %.6f" prob
                            pr "  (Related to |Kauffman bracket|^2)"
                            Ok (sprintf "%A" outcome, prob)
                        | None -> Ok ("collapsed", 0.0))
                | None -> Error "Invalid state"
            | _ -> Error "Invalid state type")

    match knotResult with
    | Ok (outcome, prob) ->
        jsonResults <- ("4_knot", box {| outcome = outcome; probability = prob |}) :: jsonResults
        csvRows <- [ "4_knot"; outcome; sprintf "%.6f" prob ] :: csvRows
    | Error msg ->
        pr "  Error: %s" msg

// ---------------------------------------------------------------------------
// Example 5 -- Builder Pattern (Layer 5)
// ---------------------------------------------------------------------------
if shouldRun 5 then
    separator ()
    pr "EXAMPLE 5: Builder Pattern (Layer 5 - Idiomatic F#)"
    separator ()

    let program = topological quantumBackend {
        do! TopologicalBuilder.initialize AnyonSpecies.AnyonType.Ising 4
        do! TopologicalBuilder.braid 0
        do! TopologicalBuilder.braid 2
        do! TopologicalBuilder.braid 0
        let! (outcome: AnyonSpecies.Particle) = TopologicalBuilder.measure 0
        return outcome
    }

    let builderResult =
        task {
            let! r = TopologicalBuilder.execute quantumBackend program
            return r
        } |> Async.AwaitTask |> Async.RunSynchronously

    match builderResult with
    | Ok outcome ->
        pr "  Builder outcome: %A" outcome
        jsonResults <- ("5_builder", box {| outcome = sprintf "%A" outcome |}) :: jsonResults
        csvRows <- [ "5_builder"; sprintf "%A" outcome ] :: csvRows
    | Error e ->
        pr "  Builder failed: %A" e

// ---------------------------------------------------------------------------
// Example 6 -- Business Application: Error Detection (Layer 6)
// ---------------------------------------------------------------------------
if shouldRun 6 then
    separator ()
    pr "EXAMPLE 6: Topological Error Detection (Layer 6 - Business)"
    separator ()

    let errorProgram = topological quantumBackend {
        do! TopologicalBuilder.initialize AnyonSpecies.AnyonType.Ising 4
        do! TopologicalBuilder.braid 0
        let! (outcome: AnyonSpecies.Particle) = TopologicalBuilder.measure 0
        let errorDetected =
            match outcome with
            | AnyonSpecies.Particle.Vacuum -> false
            | AnyonSpecies.Particle.Psi -> true
            | _ -> false
        return errorDetected
    }

    let errorResult =
        task {
            let! r = TopologicalBuilder.execute quantumBackend errorProgram
            return r
        } |> Async.AwaitTask |> Async.RunSynchronously

    match errorResult with
    | Ok hasError ->
        let status = if hasError then "ERROR DETECTED" else "No errors"
        pr "  Error detection: %s" status
        jsonResults <- ("6_error_detect", box {| errorDetected = hasError |}) :: jsonResults
        csvRows <- [ "6_error_detect"; string hasError ] :: csvRows
    | Error e ->
        pr "  Error: %A" e

// ---------------------------------------------------------------------------
// Output
// ---------------------------------------------------------------------------
if outputPath.IsSome then
    let payload =
        {| script    = "TopologicalExample.fsx"
           backend   = quantumBackend.Name
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
    pr "  dotnet fsi TopologicalExample.fsx -- --example 5"
    pr "  dotnet fsi TopologicalExample.fsx -- --quiet --output r.json --csv r.csv"
    pr "  dotnet fsi TopologicalExample.fsx -- --help"
