/// End-to-End MaxCut Example with D-Wave Backend
///
/// This example demonstrates:
/// 1. Building a MaxCut problem (graph partitioning)
/// 2. Encoding as QAOA circuit
/// 3. Creating a D-Wave annealing backend
/// 4. Execution and result interpretation
///
/// Run with: dotnet fsi DWaveMaxCutExample.fsx
///           dotnet fsi DWaveMaxCutExample.fsx -- --help

//#r "nuget: FSharp.Azure.Quantum"
#r "nuget: Microsoft.Extensions.Logging.Abstractions, 10.0.0"
#r "../../src/FSharp.Azure.Quantum/bin/Debug/net10.0/FSharp.Azure.Quantum.dll"

#load "../_common/Cli.fs"
#load "../_common/Data.fs"
#load "../_common/Reporting.fs"

open System
open FSharp.Azure.Quantum.Core.QaoaCircuit
open FSharp.Azure.Quantum.Core.CircuitAbstraction
open FSharp.Azure.Quantum.Backends.DWaveBackend
open FSharp.Azure.Quantum.Backends.DWaveTypes
open FSharp.Azure.Quantum.Examples.Common

// ---------------------------------------------------------------------------
// CLI
// ---------------------------------------------------------------------------

let argv = fsi.CommandLineArgs |> Array.skip 1
let args = Cli.parse argv

Cli.exitIfHelp
    "DWaveMaxCutExample.fsx"
    "End-to-end MaxCut solved via D-Wave simulated annealing backend."
    [ { Cli.OptionSpec.Name = "shots"
        Description = "Number of measurement shots"
        Default = Some "1000" }
      { Cli.OptionSpec.Name = "seed"
        Description = "Random seed for reproducibility"
        Default = Some "42" }
      { Cli.OptionSpec.Name = "output"
        Description = "Write results to JSON file"
        Default = None }
      { Cli.OptionSpec.Name = "csv"
        Description = "Write results to CSV file"
        Default = None }
      { Cli.OptionSpec.Name = "quiet"
        Description = "Suppress printed output"
        Default = None } ]
    args

let quiet = Cli.hasFlag "quiet" args
let numShots = Cli.getIntOr "shots" 1000 args
let seed = Cli.getIntOr "seed" 42 args
let outputPath = Cli.tryGet "output" args
let csvPath = Cli.tryGet "csv" args

// ---------------------------------------------------------------------------
// Step 1: Define the Graph
// ---------------------------------------------------------------------------

// Simple triangle graph with weighted edges
// Vertices: 0, 1, 2
// Edges: (0,1,5.0), (1,2,3.0), (0,2,4.0)
//
//     0
//    / \
//   5   4
//  /     \
// 1---3---2

let edges = [
    (0, 1, 5.0)
    (1, 2, 3.0)
    (0, 2, 4.0)
]

let numVertices = 3

if not quiet then
    printfn "============================================================"
    printfn "  MaxCut Example with D-Wave Annealing Backend"
    printfn "============================================================"
    printfn ""
    printfn "Step 1: Define the Graph"
    printfn "------------------------"
    printfn "Graph:"
    printfn "  Vertices: %d" numVertices
    printfn "  Edges:"
    for (u, v, w) in edges do
        printfn "    (%d, %d) = %.1f" u v w
    printfn ""

// ---------------------------------------------------------------------------
// Step 2: Build MaxCut QAOA Circuit
// ---------------------------------------------------------------------------

let buildMaxCutHamiltonian (nVerts: int) (edgeList: (int * int * float) list) : ProblemHamiltonian =
    let diagonalTerms =
        [ 0 .. nVerts - 1 ]
        |> List.map (fun v ->
            let weight =
                edgeList
                |> List.filter (fun (u, w, _) -> u = v || w = v)
                |> List.sumBy (fun (_, _, w) -> w)
            { Coefficient = weight / 2.0
              QubitsIndices = [| v |]
              PauliOperators = [| PauliZ |] })

    let offDiagonalTerms =
        edgeList
        |> List.map (fun (u, v, w) ->
            { Coefficient = -w / 4.0
              QubitsIndices = [| u; v |]
              PauliOperators = [| PauliZ; PauliZ |] })

    { NumQubits = nVerts
      Terms = List.append diagonalTerms offDiagonalTerms |> List.toArray }

let problemHamiltonian = buildMaxCutHamiltonian numVertices edges
let mixerHamiltonian = MixerHamiltonian.create numVertices
let qaoaParameters = [| (0.5, 0.3) |]
let qaoaCircuit = QaoaCircuit.build problemHamiltonian mixerHamiltonian qaoaParameters
let circuit = QaoaCircuitWrapper(qaoaCircuit) :> ICircuit

if not quiet then
    printfn "Step 2: Build QAOA Circuit"
    printfn "--------------------------"
    printfn "  Circuit: %d qubits" circuit.NumQubits
    printfn "  QAOA depth: p=%d" qaoaParameters.Length
    printfn ""

// ---------------------------------------------------------------------------
// Step 3: Create D-Wave Backend
// ---------------------------------------------------------------------------

let backend = MockDWaveBackend(Advantage_System6_1, seed)

if not quiet then
    printfn "Step 3: Create D-Wave Backend"
    printfn "-----------------------------"
    printfn "  Backend: Mock D-Wave Advantage_System6.1"
    printfn "  Max Qubits: %d" (getMaxQubits Advantage_System6_1)
    printfn "  Solver: %s" (getSolverName Advantage_System6_1)
    printfn "  Seed: %d" seed
    printfn ""

// ---------------------------------------------------------------------------
// Step 4: Execute on D-Wave Backend
// ---------------------------------------------------------------------------

if not quiet then
    printfn "Step 4: Execute on Backend"
    printfn "--------------------------"
    printfn "  Executing %d shots..." numShots

let results = ResizeArray<Map<string, string>>()

match backend.Execute circuit numShots with
| Error e ->
    let msg = e.Message
    if not quiet then
        printfn "  Execution error: %s" msg
    results.Add(
        [ "status", "error"
          "error", msg
          "shots", string numShots
          "seed", string seed ]
        |> Map.ofList)

| Ok execResult ->
    if not quiet then
        printfn "  Execution complete: %d shots" execResult.NumShots
        printfn "  Backend: %s" execResult.BackendName
        printfn ""

    // Count occurrence of each bitstring
    let counts =
        execResult.Measurements
        |> Array.countBy id
        |> Array.sortByDescending snd

    if not quiet then
        printfn "Step 5: Analyze Results"
        printfn "-----------------------"
        printfn "Top 5 solutions:"
        printfn "  Bitstring  | Count | Cut Value | Partition"
        printfn "  -----------|-------|-----------|----------"

    for i in 0 .. min 4 (counts.Length - 1) do
        let (bitstring, count) = counts.[i]

        let cutValue =
            edges
            |> List.filter (fun (u, v, _) -> bitstring.[u] <> bitstring.[v])
            |> List.sumBy (fun (_, _, w) -> w)

        if not quiet then
            let bitstringStr = String.Join("", bitstring)
            let partitionStr =
                [ 0 .. numVertices - 1 ]
                |> List.map (fun v -> if bitstring.[v] = 0 then sprintf "%d" v else sprintf "[%d]" v)
                |> String.concat " "
            printfn "  %s       | %5d | %9.1f | %s" bitstringStr count cutValue partitionStr

    if not quiet then printfn ""

    // Find best cut
    let bestSolution =
        counts
        |> Array.map (fun (bitstring, count) ->
            let cutValue =
                edges
                |> List.filter (fun (u, v, _) -> bitstring.[u] <> bitstring.[v])
                |> List.sumBy (fun (_, _, w) -> w)
            (bitstring, count, cutValue))
        |> Array.maxBy (fun (_, _, cutValue) -> cutValue)

    let (bestBitstring, bestCount, bestCut) = bestSolution
    let set0 = [ 0 .. numVertices - 1 ] |> List.filter (fun v -> bestBitstring.[v] = 0)
    let set1 = [ 0 .. numVertices - 1 ] |> List.filter (fun v -> bestBitstring.[v] = 1)
    let cutEdges = edges |> List.filter (fun (u, v, _) -> bestBitstring.[u] <> bestBitstring.[v])

    if not quiet then
        printfn "============================================================"
        printfn "  Best Solution Found"
        printfn "============================================================"
        printfn ""
        printfn "  Partition: %s" (String.Join("", bestBitstring))
        printfn "  Cut Value: %.1f (max possible: 12.0)" bestCut
        printfn "  Occurrences: %d/%d (%.1f%%)" bestCount numShots (100.0 * float bestCount / float numShots)
        printfn ""
        printfn "  Partition Sets:"
        printfn "    Set 0: {%s}" (set0 |> List.map string |> String.concat ", ")
        printfn "    Set 1: {%s}" (set1 |> List.map string |> String.concat ", ")
        printfn ""
        printfn "  Edges in cut:"
        for (u, v, w) in cutEdges do
            printfn "    (%d, %d) weight = %.1f" u v w
        printfn ""

    results.Add(
        [ "status", "ok"
          "shots", string numShots
          "seed", string seed
          "best_partition", String.Join("", bestBitstring)
          "best_cut_value", sprintf "%.1f" bestCut
          "best_occurrences", string bestCount
          "distinct_solutions", string counts.Length
          "set_0", (set0 |> List.map string |> String.concat ";")
          "set_1", (set1 |> List.map string |> String.concat ";")
          "cut_edges", (cutEdges |> List.map (fun (u, v, w) -> sprintf "%d-%d(%.1f)" u v w) |> String.concat ";")
          "backend", execResult.BackendName ]
        |> Map.ofList)

// ---------------------------------------------------------------------------
// Step 6: Available D-Wave Solvers (informational)
// ---------------------------------------------------------------------------

if not quiet then
    printfn "Available D-Wave Solvers"
    printfn "------------------------"
    printfn "  - Advantage_System6_1:  5640 qubits (Pegasus topology)"
    printfn "  - Advantage_System4_1:  5000 qubits (Pegasus topology)"
    printfn "  - Advantage_System1_1:  5000 qubits (Pegasus topology)"
    printfn "  - Advantage2_Prototype: 1200 qubits (Zephyr topology, next-gen)"
    printfn "  - DW_2000Q_6:           2048 qubits (Chimera topology, legacy)"
    printfn ""
    printfn "MaxCut D-Wave example complete!"

// ---------------------------------------------------------------------------
// Structured output
// ---------------------------------------------------------------------------

let resultsList = results |> Seq.toList

match outputPath with
| Some p -> Reporting.writeJson p resultsList
| None -> ()

match csvPath with
| Some p ->
    let header =
        [ "status"; "shots"; "seed"; "best_partition"; "best_cut_value"
          "best_occurrences"; "distinct_solutions"; "set_0"; "set_1"
          "cut_edges"; "backend" ]
    let rows =
        resultsList
        |> List.map (fun m ->
            header |> List.map (fun h -> m |> Map.tryFind h |> Option.defaultValue ""))
    Reporting.writeCsv p header rows
| None -> ()

if argv.Length = 0 then
    printfn ""
    printfn "Tip: run with --help for CLI options."
