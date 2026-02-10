/// Integer Variables Example - Native Integer Support for QAOA
///
/// USE CASE: Work with integer decision variables directly in quantum optimization
///
/// PROBLEM: Many real-world optimization problems involve integer variables:
/// production quantities, resource allocation, scheduling, configuration tuning.
/// This example demonstrates multiple encoding strategies (Binary, OneHot,
/// DomainWall, BoundedInteger) with automatic qubit allocation and constraint
/// enforcement.

(*
===============================================================================
 Background Theory
===============================================================================

Integer variables in QUBO require encoding into binary (qubit) representations.
The choice of encoding affects qubit count, constraint structure, and solution
quality:

  - Binary / BoundedInteger: log2(range) qubits. Value = sum of 2^k * x_k.
    Most qubit-efficient for large ranges but loses ordering locality.
  - OneHot: one qubit per value. Exactly one qubit is 1. Best for unordered
    categories (mutually exclusive choices). Constraint: sum(x_k) = 1.
  - DomainWall: (n-1) qubits for n values. A wall of 1s followed by 0s.
    Natural for ordered levels (priorities, quality tiers). Saves 1 qubit
    vs OneHot while preserving adjacency structure.

Key Equations:
  - BoundedInteger: value = low + sum_{k=0}^{ceil(log2(range))-1} 2^k * x_k
  - OneHot constraint: (sum_k x_k - 1)^2 = 0
  - DomainWall constraint: x_{k+1} <= x_k (monotone decreasing)

References:
  [1] Glover et al., "Quantum Bridge Analytics I", 4OR 17, 335-371 (2019).
  [2] Lucas, "Ising formulations of many NP problems", Front. Phys. 2 (2014).

Usage:
  dotnet fsi IntegerVariablesExample.fsx                                  (defaults)
  dotnet fsi IntegerVariablesExample.fsx -- --help
  dotnet fsi IntegerVariablesExample.fsx -- --example production
  dotnet fsi IntegerVariablesExample.fsx -- --quiet --csv results.csv
*)

//#r "nuget: FSharp.Azure.Quantum"
#r "../../src/FSharp.Azure.Quantum/bin/Debug/net10.0/FSharp.Azure.Quantum.dll"

#load "../_common/Cli.fs"
#load "../_common/Data.fs"
#load "../_common/Reporting.fs"

open FSharp.Azure.Quantum
open System
open FSharp.Azure.Quantum.Examples.Common

// ==============================================================================
// CLI ARGUMENT PARSING
// ==============================================================================

let argv = fsi.CommandLineArgs |> Array.skip 1
let args = Cli.parse argv

Cli.exitIfHelp
    "IntegerVariablesExample.fsx"
    "Integer variable encodings for quantum QAOA optimization."
    [ { Cli.OptionSpec.Name = "example"; Description = "Example: encodings|production|scheduling|routes|mixed|all"; Default = Some "encodings" }
      { Cli.OptionSpec.Name = "output";  Description = "Write results to JSON file";  Default = None }
      { Cli.OptionSpec.Name = "csv";     Description = "Write results to CSV file";    Default = None }
      { Cli.OptionSpec.Name = "quiet";   Description = "Suppress informational output"; Default = None } ]
    args

let quiet = Cli.hasFlag "quiet" args
let outputPath = Cli.tryGet "output" args
let csvPath = Cli.tryGet "csv" args
let exampleName = Cli.getOr "example" "encodings" args

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

let encodingRow
    (example: string)
    (encoding: string)
    (qubits: int)
    (detail: string) : Map<string, string> =
    Map.ofList
        [ "example",  example
          "encoding", encoding
          "qubits",   sprintf "%d" qubits
          "detail",   detail ]

// ==============================================================================
// DOMAIN TYPES
// ==============================================================================

type Product =
    { Name: string
      Profit: float
      Resource1: int
      Resource2: int }

type Task =
    { Id: string
      Duration: int
      Deadline: int }

type Route =
    { Name: string
      Distance: float
      Traffic: string }

// ==============================================================================
// EXAMPLES
// ==============================================================================

let allResults = ResizeArray<Map<string, string>>()

/// Example 1: Compare encoding strategies for range [0,15]
let runEncodings () =
    printHeader "Example 1: Encoding Strategy Comparison"

    let encodings =
        [ ("OneHot",          VariableEncoding.OneHot 16)
          ("DomainWall",      VariableEncoding.DomainWall 16)
          ("BoundedInteger",  VariableEncoding.BoundedInteger (0, 15)) ]

    if not quiet then
        printfn "  Integer range: [0, 15] (16 values)"
        printfn ""
        printfn "  %-20s | Qubits | Efficiency" "Encoding"
        printfn "  %s-+--------+-----------" (String.replicate 20 "-")

    for (name, enc) in encodings do
        let q = VariableEncoding.qubitCount enc
        let eff = 16.0 / float q
        if not quiet then
            printfn "  %-20s | %6d | %.2fx" name q eff
        allResults.Add (encodingRow "encodings" name q (sprintf "%.2fx efficiency" eff))

    if not quiet then
        printfn ""
        printfn "  Best for unordered categories: OneHot"
        printfn "  Best for ordered levels: DomainWall"
        printfn "  Best for large ranges: BoundedInteger (log scaling)"

/// Example 2: Production planning with BoundedInteger
let runProduction () =
    printHeader "Example 2: Production Planning (BoundedInteger)"

    let products =
        [ { Name = "Product A"; Profit = 50.0; Resource1 = 2; Resource2 = 1 }
          { Name = "Product B"; Profit = 40.0; Resource1 = 1; Resource2 = 2 }
          { Name = "Product C"; Profit = 60.0; Resource1 = 3; Resource2 = 1 } ]

    let maxQty = 5
    let enc = VariableEncoding.BoundedInteger (0, maxQty)
    let qPerVar = VariableEncoding.qubitCount enc
    let totalQ = qPerVar * products.Length

    if not quiet then
        printfn "  R1 available: 10, R2 available: 8"
        printfn "  Products:"
        for p in products do
            printfn "    %s: profit=$%.0f, R1=%d, R2=%d" p.Name p.Profit p.Resource1 p.Resource2
        printfn ""
        printfn "  Encoding: BoundedInteger [0, %d], %d qubits/var, %d total" maxQty qPerVar totalQ

    // Verify encode/decode roundtrip
    if not quiet then
        printfn "  Roundtrip verification:"
        for qty in [ 0; 1; 3; 5 ] do
            let bits = VariableEncoding.encode enc qty
            let decoded = VariableEncoding.decode enc bits
            let bitsStr = bits |> List.map string |> String.concat ""
            printfn "    qty %d -> %s -> %d" qty bitsStr decoded

    allResults.Add (encodingRow "production" "BoundedInteger" totalQ (sprintf "%d vars x %d qubits" products.Length qPerVar))

/// Example 3: Scheduling with DomainWall encoding
let runScheduling () =
    printHeader "Example 3: Priority-Based Scheduling (DomainWall)"

    let tasks =
        [ { Id = "Task A"; Duration = 3; Deadline = 5 }
          { Id = "Task B"; Duration = 2; Deadline = 3 }
          { Id = "Task C"; Duration = 4; Deadline = 7 }
          { Id = "Task D"; Duration = 1; Deadline = 2 } ]

    let levels = 5
    let enc = VariableEncoding.DomainWall levels
    let q = VariableEncoding.qubitCount enc

    if not quiet then
        printfn "  %d tasks, priority levels 1-%d" tasks.Length levels
        printfn "  DomainWall encoding: %d qubits (vs %d for OneHot)" q levels
        printfn "  Bit patterns:"
        for p in 1 .. levels do
            let bits = VariableEncoding.encode enc p
            let bitsStr = bits |> List.map string |> String.concat ""
            let decoded = VariableEncoding.decode enc bits
            printfn "    Priority %d: %s -> %d" p bitsStr decoded

    allResults.Add (encodingRow "scheduling" "DomainWall" (q * tasks.Length) (sprintf "%d tasks x %d levels" tasks.Length levels))

/// Example 4: Route selection with OneHot
let runRoutes () =
    printHeader "Example 4: Route Selection (OneHot)"

    let routes =
        [ { Name = "Highway";  Distance = 25.0; Traffic = "Heavy" }
          { Name = "City";     Distance = 18.0; Traffic = "Moderate" }
          { Name = "Scenic";   Distance = 35.0; Traffic = "Light" }
          { Name = "Express";  Distance = 22.0; Traffic = "Variable" } ]

    let enc = VariableEncoding.OneHot routes.Length
    let q = VariableEncoding.qubitCount enc

    if not quiet then
        printfn "  %d routes, OneHot: %d qubits (one per route)" routes.Length q
        printfn "  Constraint: exactly one bit = 1"
        printfn "  Bit patterns:"
        for i in 0 .. routes.Length - 1 do
            let bits = VariableEncoding.encode enc i
            let bitsStr = bits |> List.map string |> String.concat " "
            printfn "    %s: [%s]" routes.[i].Name bitsStr

    let constraintWeight = 10.0
    let penalty = VariableEncoding.constraintPenalty enc constraintWeight
    if not quiet then
        printfn "  Constraint penalty (weight=%.0f): diag=%.0f, off-diag=%.0f"
            constraintWeight penalty.[0, 0] penalty.[0, 1]

    allResults.Add (encodingRow "routes" "OneHot" q (sprintf "%d routes" routes.Length))

/// Example 5: Mixed integer variables
let runMixed () =
    printHeader "Example 5: Mixed Integer Variables"

    let variables =
        [ { Name = "room_booked"; VarType = BinaryVar }
          { Name = "attendees";   VarType = IntegerVar (0, 20) }
          { Name = "time_slot";   VarType = CategoricalVar ([ "Morning"; "Afternoon"; "Evening" ]) } ]

    if not quiet then
        printfn "  Conference room booking:"
        for v in variables do
            match v.VarType with
            | BinaryVar ->
                let enc = VariableEncoding.Binary
                printfn "    %s: Binary (%d qubit)" v.Name (VariableEncoding.qubitCount enc)
            | IntegerVar (lo, hi) ->
                let enc = VariableEncoding.BoundedInteger (lo, hi)
                printfn "    %s: Integer [%d,%d] (%d qubits)" v.Name lo hi (VariableEncoding.qubitCount enc)
            | CategoricalVar cats ->
                let enc = VariableEncoding.OneHot cats.Length
                printfn "    %s: Categorical %A (%d qubits)" v.Name cats (VariableEncoding.qubitCount enc)

    let quboMatrix = QuboEncoding.encodeVariables variables

    if not quiet then
        printfn "  QUBO matrix: %d total qubits" quboMatrix.Size
        printfn "  Variable names: %A" quboMatrix.VariableNames

    allResults.Add (encodingRow "mixed" "Mixed" quboMatrix.Size (sprintf "%d vars" variables.Length))

// ==============================================================================
// MAIN EXECUTION
// ==============================================================================

if not quiet then
    printfn "======================================"
    printfn "Integer Variables in Quantum QAOA"
    printfn "======================================"

match exampleName.ToLowerInvariant() with
| "all" ->
    runEncodings ()
    runProduction ()
    runScheduling ()
    runRoutes ()
    runMixed ()
| "encodings"  -> runEncodings ()
| "production" -> runProduction ()
| "scheduling" -> runScheduling ()
| "routes"     -> runRoutes ()
| "mixed"      -> runMixed ()
| other ->
    eprintfn "Unknown example: '%s'. Use: encodings|production|scheduling|routes|mixed|all" other
    exit 1

if not quiet then
    printfn ""
    printfn "======================================"
    printfn "Integer Variables Complete!"
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
    let header = [ "example"; "encoding"; "qubits"; "detail" ]
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
    printfn "   dotnet fsi IntegerVariablesExample.fsx -- --help"
    printfn "   dotnet fsi IntegerVariablesExample.fsx -- --example all"
    printfn "   dotnet fsi IntegerVariablesExample.fsx -- --quiet --output results.json"
    printfn ""
