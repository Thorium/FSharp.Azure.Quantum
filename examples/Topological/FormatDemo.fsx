(*
    .tqp Format Demo -- Topological Quantum Computing
    ====================================================

    Demonstrates the .tqp file format for topological programs:
      1. Create and save a program to .tqp
      2. Load and parse an existing .tqp file
      3. Round-trip: program -> string -> program
      4. Create programs for different anyon types
      5. Execute a .tqp file on the simulator

    Run with: dotnet fsi FormatDemo.fsx
              dotnet fsi FormatDemo.fsx -- --example 3
              dotnet fsi FormatDemo.fsx -- --quiet --output r.json --csv r.csv
*)

#r "../../src/FSharp.Azure.Quantum/bin/Debug/net10.0/FSharp.Azure.Quantum.dll"
#r "../../src/FSharp.Azure.Quantum.Topological/bin/Debug/net10.0/FSharp.Azure.Quantum.Topological.dll"
#load "../_common/Cli.fs"
#load "../_common/Data.fs"
#load "../_common/Reporting.fs"

open System
open System.IO
open FSharp.Azure.Quantum.Topological
open FSharp.Azure.Quantum.Topological.TopologicalFormat
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Examples.Common

// ---------------------------------------------------------------------------
// CLI
// ---------------------------------------------------------------------------
let argv = fsi.CommandLineArgs |> Array.skip 1
let args = Cli.parse argv

Cli.exitIfHelp "FormatDemo.fsx" ".tqp file format import, export, and execution"
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
// Quantum backend (Rule 1) -- topological IQuantumBackend
// ---------------------------------------------------------------------------
let quantumBackend = TopologicalUnifiedBackendFactory.createIsing 10

// Results accumulators
let mutable jsonResults : (string * obj) list = []
let mutable csvRows     : string list list    = []

// Temp file tracking for cleanup
let mutable tempFiles : string list = []

// ---------------------------------------------------------------------------
// Example 1 -- Create and save a .tqp program
// ---------------------------------------------------------------------------
if shouldRun 1 then
    separator ()
    pr "EXAMPLE 1: Create and save .tqp file"
    separator ()

    let program = {
        AnyonType = AnyonSpecies.AnyonType.Fibonacci
        Operations = [ Initialize 2; Braid 0; Measure 0 ]
    }

    let tqpPath = "fibonacci-simple.tqp"
    match Serializer.serializeToFile program tqpPath with
    | Ok () ->
        tempFiles <- tqpPath :: tempFiles
        let content = File.ReadAllText(tqpPath)
        let lineCount = content.Split('\n').Length
        pr "Saved to: %s (%d lines)" tqpPath lineCount
        pr "  Anyon type: Fibonacci"
        pr "  Operations: Init 2, Braid 0, Measure 0"

        jsonResults <- ("1_save", box {| file = tqpPath; lines = lineCount; anyon = "Fibonacci" |}) :: jsonResults
        csvRows <- [ "1_save"; tqpPath; string lineCount; "Fibonacci" ] :: csvRows
    | Error msg ->
        pr "Failed: %s" msg

// ---------------------------------------------------------------------------
// Example 2 -- Load and parse a .tqp file
// ---------------------------------------------------------------------------
if shouldRun 2 then
    separator ()
    pr "EXAMPLE 2: Load and parse .tqp file"
    separator ()

    let bellPath = "bell-state.tqp"
    match Parser.parseFile bellPath with
    | Ok program ->
        let opCount = program.Operations.Length
        let nonComment =
            program.Operations
            |> List.filter (fun op -> match op with Comment _ -> false | _ -> true)
            |> List.length
        pr "Loaded: %s" bellPath
        pr "  Anyon type:  %A" program.AnyonType
        pr "  Operations:  %d total (%d non-comment)" opCount nonComment
        for op in program.Operations do
            match op with
            | Comment _   -> ()
            | Initialize c -> pr "    INIT %d" c
            | Braid i      -> pr "    BRAID %d" i
            | Measure i    -> pr "    MEASURE %d" i
            | FMove (d,n)  -> pr "    FMOVE %A %d" d n

        jsonResults <- ("2_load", box {| file = bellPath; ops = opCount; nonComment = nonComment |}) :: jsonResults
        csvRows <- [ "2_load"; bellPath; string opCount; string nonComment ] :: csvRows
    | Error msg ->
        pr "Parse failed: %s" msg

// ---------------------------------------------------------------------------
// Example 3 -- Round-trip (program -> string -> program)
// ---------------------------------------------------------------------------
if shouldRun 3 then
    separator ()
    pr "EXAMPLE 3: Round-trip serialization"
    separator ()

    let original = {
        AnyonType = AnyonSpecies.AnyonType.Ising
        Operations = [
            Comment "# Ising algorithm"
            Initialize 6; Braid 0; Braid 2; Braid 4
            FMove (FMoveDirection.Left, 1)
            Measure 1; Measure 3
        ]
    }

    let serialized = Serializer.serializeProgram original
    match Parser.parseProgram serialized with
    | Ok parsed ->
        let typeMatch = original.AnyonType = parsed.AnyonType
        let origOps = original.Operations.Length
        let parsedOps =
            parsed.Operations
            |> List.filter (fun op -> match op with Comment c when c.Contains("Generated:") -> false | _ -> true)
            |> List.length
        pr "Round-trip: %s" (if typeMatch then "SUCCESS" else "TYPE MISMATCH")
        pr "  Original ops:  %d" origOps
        pr "  Parsed ops:    %d" parsedOps

        jsonResults <- ("3_roundtrip", box {| typeMatch = typeMatch; origOps = origOps; parsedOps = parsedOps |}) :: jsonResults
        csvRows <- [ "3_roundtrip"; string typeMatch; string origOps; string parsedOps ] :: csvRows
    | Error msg ->
        pr "Round-trip failed: %s" msg

// ---------------------------------------------------------------------------
// Example 4 -- Different anyon types
// ---------------------------------------------------------------------------
if shouldRun 4 then
    separator ()
    pr "EXAMPLE 4: Programs for different anyon types"
    separator ()

    let types = [
        ("Ising",     AnyonSpecies.AnyonType.Ising)
        ("Fibonacci", AnyonSpecies.AnyonType.Fibonacci)
        ("SU(2)_3",   AnyonSpecies.AnyonType.SU2Level 3)
    ]

    for (name, anyonType) in types do
        let program = {
            AnyonType = anyonType
            Operations = [ Comment $"# {name} example"; Initialize 4; Braid 0; Braid 1; Measure 0 ]
        }
        let cleanName = name.ToLowerInvariant().Replace("(","").Replace(")","").Replace("_","-")
        let filename = $"{cleanName}-example.tqp"
        match Serializer.serializeToFile program filename with
        | Ok () ->
            tempFiles <- filename :: tempFiles
            pr "  Created: %s (%s)" filename name
        | Error msg ->
            pr "  Failed %s: %s" filename msg

    jsonResults <- ("4_types", box {| count = types.Length |}) :: jsonResults
    csvRows <- [ "4_types"; string types.Length ] :: csvRows

// ---------------------------------------------------------------------------
// Example 5 -- Execute a .tqp file on simulator
// ---------------------------------------------------------------------------
if shouldRun 5 then
    separator ()
    pr "EXAMPLE 5: Execute .tqp file on simulator"
    separator ()

    // Ensure we have the file (create if example 1 was skipped)
    let tqpPath = "fibonacci-simple.tqp"
    if not (File.Exists tqpPath) then
        let program = {
            AnyonType = AnyonSpecies.AnyonType.Fibonacci
            Operations = [ Initialize 2; Braid 0; Measure 0 ]
        }
        Serializer.serializeToFile program tqpPath |> ignore
        tempFiles <- tqpPath :: tempFiles

    let topoBackend =
        TopologicalBackend.SimulatorBackend(AnyonSpecies.AnyonType.Fibonacci, 10)
        :> TopologicalBackend.ITopologicalBackend

    let execResult =
        task {
            match Parser.parseFile tqpPath with
            | Ok program ->
                let! r = Executor.executeProgram topoBackend program
                return Some r
            | Error _ -> return None
        } |> Async.AwaitTask |> Async.RunSynchronously

    match execResult with
    | Some (Ok result) ->
        let measCount = result.MeasurementOutcomes.Length
        pr "Execution: SUCCESS"
        pr "  Measurements: %d" measCount
        for (outcome, prob) in result.MeasurementOutcomes do
            pr "    %A (prob: %.4f)" outcome prob

        jsonResults <- ("5_execute", box {| status = "ok"; measurements = measCount |}) :: jsonResults
        csvRows <- [ "5_execute"; "ok"; string measCount ] :: csvRows
    | Some (Error err) ->
        pr "Execution failed: %s" err.Message
    | None ->
        pr "Parse failed"

// ---------------------------------------------------------------------------
// Cleanup temp files
// ---------------------------------------------------------------------------
for f in tempFiles do
    if File.Exists f then File.Delete f

// ---------------------------------------------------------------------------
// Output
// ---------------------------------------------------------------------------
if outputPath.IsSome then
    let payload =
        {| script    = "FormatDemo.fsx"
           backend   = "Topological (Ising)"
           timestamp = DateTime.UtcNow.ToString("o")
           example   = exChoice
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
    pr "  dotnet fsi FormatDemo.fsx -- --example 5"
    pr "  dotnet fsi FormatDemo.fsx -- --quiet --output r.json --csv r.csv"
    pr "  dotnet fsi FormatDemo.fsx -- --help"
