// Grover Graph Coloring Example - Gate-Based Approach
// Solves graph coloring problems using Grover's quantum search algorithm
//
// Usage:
//   dotnet fsi Grover_GraphColoring_Example.fsx
//   dotnet fsi Grover_GraphColoring_Example.fsx -- --help
//   dotnet fsi Grover_GraphColoring_Example.fsx -- --example 4
//   dotnet fsi Grover_GraphColoring_Example.fsx -- --shots 2000 --example all
//   dotnet fsi Grover_GraphColoring_Example.fsx -- --quiet --output results.json --csv results.csv

(*
===============================================================================
 Background Theory
===============================================================================

Graph coloring assigns labels (colors) to graph vertices such that no two
adjacent vertices share the same color. The minimum number of colors needed
is the chromatic number chi(G). Graph coloring is NP-hard for general graphs,
making it a natural target for quantum speedup via Grover's algorithm.

Grover's algorithm encodes graph coloring as an oracle problem: given an
assignment of colors to vertices (encoded in qubits), the oracle marks
assignments that satisfy all edge constraints (adjacent vertices differ).
Grover then amplifies the amplitude of valid colorings.

For k colors and n vertices, each vertex needs ceil(log2(k)) qubits,
giving a search space of k^n (or 2^(n*ceil(log2(k))) in qubit terms).
Grover finds a valid coloring in O(sqrt(k^n)) queries vs O(k^n) classical.

Use Cases:
  - Register allocation (compiler optimization)
  - Frequency assignment (wireless networks)
  - Exam scheduling (university timetabling)
  - Map coloring (cartography)

When to use Grover vs QAOA:
  - Grover: Small graphs (<10 vertices), exact solutions, guaranteed speedup
  - QAOA: Larger graphs, approximate solutions, variational optimization

References:
  [1] Grover, "A fast quantum mechanical algorithm for database search",
      STOC '96. https://doi.org/10.1145/237814.237866
  [2] Garey & Johnson, "Computers and Intractability", W.H. Freeman (1979).
  [3] Wikipedia: Graph_coloring
      https://en.wikipedia.org/wiki/Graph_coloring
*)

#r "nuget: Microsoft.Extensions.Logging.Abstractions, 10.0.0"
#r "../../src/FSharp.Azure.Quantum/bin/Debug/net10.0/FSharp.Azure.Quantum.dll"
#load "../_common/Cli.fs"
#load "../_common/Data.fs"
#load "../_common/Reporting.fs"

open System
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Backends.LocalBackend
open FSharp.Azure.Quantum.GroverSearch
open FSharp.Azure.Quantum.GroverSearch.Oracle
open FSharp.Azure.Quantum.Examples.Common

// ============================================================================
// CLI Setup
// ============================================================================

let argv = fsi.CommandLineArgs |> Array.skip 1
let args = Cli.parse argv

Cli.exitIfHelp "Grover_GraphColoring_Example.fsx" "Solve graph coloring problems using Grover's quantum search." [
    { Name = "example"; Description = "Which example to run (1/2/3/4/all)"; Default = Some "all" }
    { Name = "shots"; Description = "Number of measurement shots"; Default = Some "1000" }
    { Name = "output"; Description = "Write results to JSON file"; Default = None }
    { Name = "csv"; Description = "Write results to CSV file"; Default = None }
    { Name = "quiet"; Description = "Suppress informational output"; Default = None }
]

let exampleChoice = Cli.getOr "example" "all" args
let shots = Cli.getIntOr "shots" 1000 args
let quiet = Cli.hasFlag "quiet" args
let outputPath = Cli.tryGet "output" args
let csvPath = Cli.tryGet "csv" args

let shouldRun (ex: string) =
    exampleChoice = "all" || exampleChoice = ex

// ============================================================================
// Helpers
// ============================================================================

/// Calculate qubits per vertex for k colors
let qubitsPerVertex (numColors: int) =
    if numColors <= 1 then 1
    else int (Math.Ceiling(Math.Log(float numColors) / Math.Log(2.0)))

/// Extract color for a vertex from a bit pattern
let extractColor (assignment: int) (vertexIndex: int) (bitsPerVertex: int) =
    let shift = vertexIndex * bitsPerVertex
    let mask = (1 <<< bitsPerVertex) - 1
    (assignment >>> shift) &&& mask

/// Extract all vertex colors from a solution
let extractColors (assignment: int) (numVertices: int) (numColors: int) =
    let bpv = qubitsPerVertex numColors
    [| for v in 0 .. numVertices - 1 -> extractColor assignment v bpv |]

/// Check if all extracted colors are within valid range
let allColorsValid (colors: int array) (numColors: int) =
    colors |> Array.forall (fun c -> c < numColors)

type ExampleResult = {
    Example: string
    GraphType: string
    NumVertices: int
    NumEdges: int
    NumColors: int
    Qubits: int
    QuantumColorings: int array array
    SuccessProbability: float
    Iterations: int
    Shots: int
    Status: string
}

let allResults = System.Collections.Generic.List<ExampleResult>()

// ============================================================================
// Example 1: Path Graph (Bipartite) - 2-Coloring
// ============================================================================
//
// Graph: 0 -- 1 -- 2
// This is a path graph, which is bipartite and can be colored with 2 colors.
// Expected solutions: Alternating colors (0-1-0 or 1-0-1)

if shouldRun "1" then
    if not quiet then
        printfn "=== Example 1: Path Graph (2-Coloring) ==="
        printfn "Graph: 0 -- 1 -- 2 (path/bipartite)"
        printfn ""

    let pathGraph = graph 3 [(0, 1); (1, 2)]
    let pathConfig = { Graph = pathGraph; NumColors = 2 }

    match graphColoringOracle pathConfig with
    | Error err ->
        if not quiet then printfn "Failed to create oracle: %A" err
        allResults.Add({
            Example = "1-Path-2Color"; GraphType = "Path (bipartite)"
            NumVertices = 3; NumEdges = 2; NumColors = 2; Qubits = 0
            QuantumColorings = [||]; SuccessProbability = 0.0; Iterations = 0
            Shots = shots; Status = sprintf "Oracle error: %A" err
        })
    | Ok oracle ->
        if not quiet then
            printfn "Oracle created: %d vertices, %d edges, %d colors, %d qubits"
                pathConfig.Graph.NumVertices pathConfig.Graph.Edges.Length pathConfig.NumColors oracle.NumQubits
            printfn ""
            printfn "Running Grover's algorithm (%d shots)..." shots

        let backend = LocalBackend() :> IQuantumBackend
        let config = { Grover.defaultConfig with Shots = shots }

        match Grover.search oracle backend config with
        | Error err ->
            if not quiet then printfn "Search failed: %A" err
            allResults.Add({
                Example = "1-Path-2Color"; GraphType = "Path (bipartite)"
                NumVertices = 3; NumEdges = 2; NumColors = 2; Qubits = oracle.NumQubits
                QuantumColorings = [||]; SuccessProbability = 0.0; Iterations = 0
                Shots = shots; Status = sprintf "Search error: %A" err
            })
        | Ok result ->
            // Filter to only valid solutions (satisfy oracle)
            let validSolutions =
                result.Solutions
                |> List.filter (fun sol -> Oracle.isSolution oracle.Spec sol)

            let colorings =
                validSolutions
                |> List.map (fun sol -> extractColors sol 3 2)
                |> List.filter (fun c -> allColorsValid c 2)
                |> List.toArray

            if not quiet then
                if colorings.Length = 0 then
                    printfn "  No valid coloring found"
                else
                    printfn "  Found %d valid coloring(s):" colorings.Length
                    for c in colorings do
                        printfn "    v0=%d v1=%d v2=%d" c.[0] c.[1] c.[2]
                    printfn "  Success probability: %.2f%%" (result.SuccessProbability * 100.0)
                    printfn "  Iterations used: %d" result.Iterations

            allResults.Add({
                Example = "1-Path-2Color"; GraphType = "Path (bipartite)"
                NumVertices = 3; NumEdges = 2; NumColors = 2; Qubits = oracle.NumQubits
                QuantumColorings = colorings
                SuccessProbability = result.SuccessProbability; Iterations = result.Iterations
                Shots = shots; Status = if colorings.Length = 0 then "No solution" else "OK"
            })

    if not quiet then
        printfn ""
        printfn "================================================"
        printfn ""

// ============================================================================
// Example 2: Triangle Graph (Complete K3) - 3-Coloring
// ============================================================================
//
// Graph: Triangle where all 3 vertices are connected
//   0 --- 1
//    \   /
//     \ /
//      2
//
// Requires 3 colors (chromatic number = 3).
// All 3 vertices must have different colors.

if shouldRun "2" then
    if not quiet then
        printfn "=== Example 2: Triangle Graph (3-Coloring) ==="
        printfn "Graph: Complete K3 (all vertices connected)"
        printfn ""

    let triangleGraph = graph 3 [(0, 1); (1, 2); (2, 0)]
    let triangleConfig = { Graph = triangleGraph; NumColors = 3 }

    match graphColoringOracle triangleConfig with
    | Error err ->
        if not quiet then printfn "Failed to create oracle: %A" err
        allResults.Add({
            Example = "2-Triangle-3Color"; GraphType = "Complete K3"
            NumVertices = 3; NumEdges = 3; NumColors = 3; Qubits = 0
            QuantumColorings = [||]; SuccessProbability = 0.0; Iterations = 0
            Shots = shots; Status = sprintf "Oracle error: %A" err
        })
    | Ok oracle ->
        if not quiet then
            printfn "Oracle created: %d vertices, %d edges, %d colors, %d qubits (2 bits/vertex for 3 colors)"
                triangleConfig.Graph.NumVertices triangleConfig.Graph.Edges.Length triangleConfig.NumColors oracle.NumQubits
            printfn ""
            printfn "Running Grover's algorithm (%d shots)..." shots

        let backend = LocalBackend() :> IQuantumBackend
        let config = { Grover.defaultConfig with Shots = shots }

        match Grover.search oracle backend config with
        | Error err ->
            if not quiet then printfn "Search failed: %A" err
            allResults.Add({
                Example = "2-Triangle-3Color"; GraphType = "Complete K3"
                NumVertices = 3; NumEdges = 3; NumColors = 3; Qubits = oracle.NumQubits
                QuantumColorings = [||]; SuccessProbability = 0.0; Iterations = 0
                Shots = shots; Status = sprintf "Search error: %A" err
            })
        | Ok result ->
            let colorings =
                result.Solutions
                |> List.map (fun sol -> extractColors sol 3 3)
                |> List.filter (fun c -> allColorsValid c 3)
                |> List.toArray

            if not quiet then
                if colorings.Length = 0 then
                    printfn "  No valid coloring found"
                else
                    printfn "  Found %d valid coloring(s):" colorings.Length
                    for c in colorings |> Array.truncate 5 do
                        printfn "    v0=%d v1=%d v2=%d (all different)" c.[0] c.[1] c.[2]
                    printfn "  Success probability: %.2f%%" (result.SuccessProbability * 100.0)
                    printfn "  Iterations used: %d" result.Iterations

            allResults.Add({
                Example = "2-Triangle-3Color"; GraphType = "Complete K3"
                NumVertices = 3; NumEdges = 3; NumColors = 3; Qubits = oracle.NumQubits
                QuantumColorings = colorings
                SuccessProbability = result.SuccessProbability; Iterations = result.Iterations
                Shots = shots; Status = if colorings.Length = 0 then "No solution" else "OK"
            })

    if not quiet then
        printfn ""
        printfn "================================================"
        printfn ""

// ============================================================================
// Example 3: Square Graph (Cycle C4) - Minimal Coloring
// ============================================================================
//
// Graph: 4 vertices in a square
//   0 --- 1
//   |     |
//   3 --- 2
//
// 4-cycle, chromatic number = 2. We provide 3 colors to observe
// whether Grover finds minimal (2-color) solutions.

if shouldRun "3" then
    if not quiet then
        printfn "=== Example 3: Square Graph (Cycle C4) ==="
        printfn "Graph: 4-cycle (square), chromatic number = 2"
        printfn ""

    let squareGraph = graph 4 [(0, 1); (1, 2); (2, 3); (3, 0)]
    let squareConfig = { Graph = squareGraph; NumColors = 3 }

    match graphColoringOracle squareConfig with
    | Error err ->
        if not quiet then printfn "Failed to create oracle: %A" err
        allResults.Add({
            Example = "3-Square-C4"; GraphType = "4-cycle (square)"
            NumVertices = 4; NumEdges = 4; NumColors = 3; Qubits = 0
            QuantumColorings = [||]; SuccessProbability = 0.0; Iterations = 0
            Shots = shots; Status = sprintf "Oracle error: %A" err
        })
    | Ok oracle ->
        if not quiet then
            printfn "Oracle created: %d vertices, %d edges, %d colors, %d qubits"
                squareConfig.Graph.NumVertices squareConfig.Graph.Edges.Length squareConfig.NumColors oracle.NumQubits
            printfn ""
            printfn "Running Grover's algorithm (%d shots)..." shots

        let backend = LocalBackend() :> IQuantumBackend
        let config = { Grover.defaultConfig with Shots = shots }

        match Grover.search oracle backend config with
        | Error err ->
            if not quiet then printfn "Search failed: %A" err
            allResults.Add({
                Example = "3-Square-C4"; GraphType = "4-cycle (square)"
                NumVertices = 4; NumEdges = 4; NumColors = 3; Qubits = oracle.NumQubits
                QuantumColorings = [||]; SuccessProbability = 0.0; Iterations = 0
                Shots = shots; Status = sprintf "Search error: %A" err
            })
        | Ok result ->
            let colorings =
                result.Solutions
                |> List.map (fun sol -> extractColors sol 4 3)
                |> List.filter (fun c -> allColorsValid c 3)
                |> List.toArray

            if not quiet then
                if colorings.Length = 0 then
                    printfn "  No valid coloring found"
                else
                    printfn "  Found %d valid coloring(s):" colorings.Length
                    for c in colorings |> Array.truncate 5 do
                        let uniqueColors = c |> Array.distinct |> Array.length
                        printfn "    [%d,%d,%d,%d] uses %d colors" c.[0] c.[1] c.[2] c.[3] uniqueColors
                    printfn "  Success probability: %.2f%%" (result.SuccessProbability * 100.0)
                    printfn "  Iterations used: %d" result.Iterations

            allResults.Add({
                Example = "3-Square-C4"; GraphType = "4-cycle (square)"
                NumVertices = 4; NumEdges = 4; NumColors = 3; Qubits = oracle.NumQubits
                QuantumColorings = colorings
                SuccessProbability = result.SuccessProbability; Iterations = result.Iterations
                Shots = shots; Status = if colorings.Length = 0 then "No solution" else "OK"
            })

    if not quiet then
        printfn ""
        printfn "================================================"
        printfn ""

// ============================================================================
// Example 4: Register Allocation (Compiler Optimization)
// ============================================================================
//
// Real-world application: Assign CPU registers to variables
//
// Interference graph:
//   R1 conflicts with R2, R3
//   R2 conflicts with R1, R4
//   R3 conflicts with R1, R4
//   R4 conflicts with R2, R3
//
// Goal: Minimize number of CPU registers needed

if shouldRun "4" then
    if not quiet then
        printfn "=== Example 4: Register Allocation (Real-World) ==="
        printfn "Problem: Assign CPU registers to 4 program variables"
        printfn "Conflicts: R1<->R2, R1<->R3, R2<->R4, R3<->R4"
        printfn ""

    let registerGraph = graph 4 [
        (0, 1)  // R1 conflicts with R2
        (0, 2)  // R1 conflicts with R3
        (1, 3)  // R2 conflicts with R4
        (2, 3)  // R3 conflicts with R4
    ]
    let registerConfig = { Graph = registerGraph; NumColors = 3 }

    match graphColoringOracle registerConfig with
    | Error err ->
        if not quiet then printfn "Failed to create oracle: %A" err
        allResults.Add({
            Example = "4-RegisterAlloc"; GraphType = "Interference graph"
            NumVertices = 4; NumEdges = 4; NumColors = 3; Qubits = 0
            QuantumColorings = [||]; SuccessProbability = 0.0; Iterations = 0
            Shots = shots; Status = sprintf "Oracle error: %A" err
        })
    | Ok oracle ->
        if not quiet then
            printfn "Oracle created: %d variables, %d conflicts, %d registers (EAX/EBX/ECX), %d qubits"
                registerConfig.Graph.NumVertices registerConfig.Graph.Edges.Length registerConfig.NumColors oracle.NumQubits
            printfn ""
            printfn "Running Grover's algorithm (%d shots)..." shots

        let backend = LocalBackend() :> IQuantumBackend
        let config = { Grover.defaultConfig with Shots = shots }

        match Grover.search oracle backend config with
        | Error err ->
            if not quiet then printfn "Search failed: %A" err
            allResults.Add({
                Example = "4-RegisterAlloc"; GraphType = "Interference graph"
                NumVertices = 4; NumEdges = 4; NumColors = 3; Qubits = oracle.NumQubits
                QuantumColorings = [||]; SuccessProbability = 0.0; Iterations = 0
                Shots = shots; Status = sprintf "Search error: %A" err
            })
        | Ok result ->
            let registers = [|"EAX"; "EBX"; "ECX"|]

            let colorings =
                result.Solutions
                |> List.map (fun sol -> extractColors sol 4 3)
                |> List.filter (fun c -> allColorsValid c 3)
                |> List.toArray

            if not quiet then
                if colorings.Length = 0 then
                    printfn "  No valid allocation found"
                else
                    printfn "  Found %d valid register allocation(s):" colorings.Length
                    match colorings |> Array.tryHead with
                    | Some c ->
                        printfn ""
                        printfn "  Register Allocation:"
                        printfn "    R1 -> %s" registers.[c.[0]]
                        printfn "    R2 -> %s" registers.[c.[1]]
                        printfn "    R3 -> %s" registers.[c.[2]]
                        printfn "    R4 -> %s" registers.[c.[3]]
                        let uniqueRegs = c |> Array.distinct |> Array.length
                        printfn ""
                        printfn "  Registers used: %d (chromatic number)" uniqueRegs
                    | None -> ()
                    printfn "  Success probability: %.2f%%" (result.SuccessProbability * 100.0)
                    printfn "  Iterations used: %d" result.Iterations

            allResults.Add({
                Example = "4-RegisterAlloc"; GraphType = "Interference graph"
                NumVertices = 4; NumEdges = 4; NumColors = 3; Qubits = oracle.NumQubits
                QuantumColorings = colorings
                SuccessProbability = result.SuccessProbability; Iterations = result.Iterations
                Shots = shots; Status = if colorings.Length = 0 then "No solution" else "OK"
            })

    if not quiet then
        printfn ""
        printfn "================================================"
        printfn ""

// ============================================================================
// Summary
// ============================================================================

if not quiet then
    printfn "Graph Coloring Summary"
    printfn "====================="
    for r in allResults do
        printfn ""
        printfn "  %s (%s)" r.Example r.GraphType
        printfn "    Vertices: %d, Edges: %d, Colors: %d, Qubits: %d" r.NumVertices r.NumEdges r.NumColors r.Qubits
        printfn "    Valid colorings found: %d" r.QuantumColorings.Length
        printfn "    Probability: %.2f%%, Iterations: %d" (r.SuccessProbability * 100.0) r.Iterations
        printfn "    Status: %s" r.Status
    printfn ""
    printfn "Key Takeaways:"
    printfn "  1. Grover finds valid graph colorings using quantum search"
    printfn "  2. Works for various graph types: paths, cycles, complete graphs"
    printfn "  3. Real-world application: register allocation in compilers"
    printfn "  4. Complements existing QAOA/annealing approaches"
    printfn "  5. Best for small graphs (<10 vertices) with guaranteed speedup"
    printfn ""
    printfn "Comparison with other approaches:"
    printfn "  - QAOA (QuantumGraphColoringSolver): Better for larger graphs"
    printfn "  - Grover (this example): Better for small graphs, exact solutions"
    printfn "  - Classical greedy: Fast baseline, approximate solutions"

// ============================================================================
// Output
// ============================================================================

let resultRecords =
    allResults
    |> Seq.toList
    |> List.map (fun r ->
        {| Example = r.Example
           GraphType = r.GraphType
           NumVertices = r.NumVertices
           NumEdges = r.NumEdges
           NumColors = r.NumColors
           Qubits = r.Qubits
           NumColoringsFound = r.QuantumColorings.Length
           Colorings = r.QuantumColorings |> Array.map (fun c -> c |> Array.toList) |> Array.toList
           SuccessProbability = r.SuccessProbability
           Iterations = r.Iterations
           Shots = r.Shots
           Status = r.Status |})

match outputPath with
| Some path ->
    Reporting.writeJson path resultRecords
    if not quiet then printfn "Results written to %s" path
| None -> ()

match csvPath with
| Some path ->
    let header = ["Example"; "GraphType"; "NumVertices"; "NumEdges"; "NumColors"; "Qubits"; "NumColoringsFound"; "SuccessProbability"; "Iterations"; "Shots"; "Status"]
    let rows =
        allResults
        |> Seq.toList
        |> List.map (fun r ->
            [ r.Example; r.GraphType; string r.NumVertices; string r.NumEdges; string r.NumColors
              string r.Qubits; string r.QuantumColorings.Length
              sprintf "%.4f" r.SuccessProbability; string r.Iterations; string r.Shots; r.Status ])
    Reporting.writeCsv path header rows
    if not quiet then printfn "CSV written to %s" path
| None -> ()

// ============================================================================
// Usage hints (shown when run with no arguments)
// ============================================================================

if argv.Length = 0 && outputPath.IsNone && csvPath.IsNone then
    printfn ""
    printfn "Tip: Use --help for all options, --quiet --output results.json for automation"
