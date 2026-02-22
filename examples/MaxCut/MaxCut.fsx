/// MaxCut Example - Circuit Design Wire Minimization
/// 
/// USE CASE: Partition circuit blocks to minimize wire crossings
/// 
/// PROBLEM: Given a circuit design with interconnected blocks,
/// partition them into two regions to minimize communication overhead.
/// 
/// This is THE canonical QAOA problem - MaxCut is a fundamental
/// graph partitioning problem with applications in:
/// - VLSI circuit design (minimize wire crossings)
/// - Social network analysis (detect communities)
/// - Load balancing (minimize inter-server communication)
/// - Image segmentation (foreground/background separation)

(*
===============================================================================
 Background Theory
===============================================================================

The Maximum Cut (MaxCut) problem is a fundamental combinatorial optimization
problem: given a weighted graph G = (V, E, w), partition the vertices into two
disjoint sets S and T such that the sum of edge weights crossing the partition
is maximized. MaxCut is NP-hard, meaning no known classical algorithm can solve
all instances efficiently. The best classical approximation algorithm (Goemans-
Williamson, 1995) achieves a 0.878 approximation ratio using semidefinite
programming, but exact solutions require exponential time in the worst case.

The Quantum Approximate Optimization Algorithm (QAOA), introduced by Farhi et al.
(2014), is a variational quantum algorithm specifically designed for combinatorial
optimization problems like MaxCut. QAOA encodes the problem Hamiltonian H_C (cost)
and a mixer Hamiltonian H_B (driver) into alternating quantum operations. At depth
p, the ansatz is: |Ïˆ(Î³,Î²)âŸ© = Î â‚– exp(-iÎ²â‚–H_B)Â·exp(-iÎ³â‚–H_C)|+âŸ©â¿. The parameters
(Î³,Î²) are optimized classically to maximize âŸ¨H_CâŸ©.

Key Equations:
  - MaxCut cost function: C(z) = Î£_{(i,j)âˆˆE} wáµ¢â±¼Â·Â½(1 - záµ¢zâ±¼)  where záµ¢ âˆˆ {Â±1}
  - Problem Hamiltonian: H_C = Î£_{(i,j)âˆˆE} wáµ¢â±¼Â·Â½(I - Záµ¢Zâ±¼)
  - Mixer Hamiltonian: H_B = Î£áµ¢ Xáµ¢ (induces transitions between configurations)
  - QAOA depth-p ansatz: |Î³,Î²âŸ© = Î â‚–â‚Œâ‚áµ– e^{-iÎ²â‚–H_B} e^{-iÎ³â‚–H_C} |+âŸ©â¿
  - Expected cut value: âŸ¨Î³,Î²|H_C|Î³,Î²âŸ© (maximized over parameters)

Quantum Advantage:
  QAOA provides a quantum-native approach to NP-hard optimization. At depth pâ†’âˆž,
  QAOA provably finds the optimal solution. For finite depth, QAOA can outperform
  classical local search on certain graph instances. On NISQ devices, QAOA at
  depth p=1-3 often matches or exceeds classical heuristics for small graphs.
  The key advantage is parallel exploration of the solution space via quantum
  superposition and interference, potentially finding high-quality solutions
  faster than classical random sampling or greedy algorithms.

References:
  [1] Farhi, Goldstone, Gutmann, "A Quantum Approximate Optimization Algorithm",
      arXiv:1411.4028 (2014). https://arxiv.org/abs/1411.4028
  [2] Goemans & Williamson, "Improved approximation algorithms for maximum cut",
      J. ACM 42(6), 1115-1145 (1995). https://doi.org/10.1145/227683.227684
  [3] Zhou et al., "Quantum Approximate Optimization Algorithm: Performance,
      Mechanism, and Implementation", Phys. Rev. X 10, 021067 (2020).
      https://doi.org/10.1103/PhysRevX.10.021067
  [4] Wikipedia: Maximum_cut
      https://en.wikipedia.org/wiki/Maximum_cut
*)

//#r "nuget: FSharp.Azure.Quantum"
#r "nuget: Microsoft.Extensions.Logging.Abstractions, 10.0.0"
#r "../../src/FSharp.Azure.Quantum/bin/Debug/net10.0/FSharp.Azure.Quantum.dll"

#load "../_common/Cli.fs"
#load "../_common/Data.fs"
#load "../_common/Reporting.fs"

open System
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.Examples.Common

// ---------------------------------------------------------------------------
// CLI
// ---------------------------------------------------------------------------

let argv = fsi.CommandLineArgs |> Array.skip 1
let args = Cli.parse argv

Cli.exitIfHelp
    "MaxCut.fsx"
    "Solve Maximum Cut problems using quantum QAOA optimization."
    [ { Cli.OptionSpec.Name = "example"
        Description = "Example to run: circuit|helpers|social|triangle|k3|all"
        Default = Some "all" }
      { Cli.OptionSpec.Name = "input"
        Description = "CSV file with graph edges (columns: source,target,weight)"
        Default = None }
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
let exampleName = Cli.getOr "example" "all" args
let inputPath = Cli.tryGet "input" args
let outputPath = Cli.tryGet "output" args
let csvPath = Cli.tryGet "csv" args

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

/// Build a structured result row from a MaxCut solution.
let resultRow
    (example: string)
    (vertices: string list)
    (edgeCount: int)
    (solution: MaxCut.Solution)
    : Map<string, string> =
    [ "example", example
      "vertices", string vertices.Length
      "edges", string edgeCount
      "cut_value", sprintf "%.1f" solution.CutValue
      "cut_edges", string solution.CutEdges.Length
      "partition_s", (solution.PartitionS |> String.concat ";")
      "partition_t", (solution.PartitionT |> String.concat ";")
      "backend", solution.BackendName ]
    |> Map.ofList

/// Solve a MaxCut problem and print results. Returns a result row on success.
let solveAndReport
    (example: string)
    (vertices: string list)
    (problem: MaxCut.MaxCutProblem)
    : Map<string, string> option =
    match MaxCut.solve problem None with
    | Ok solution ->
        if not quiet then
            printfn "  Partition A: %A" solution.PartitionS
            printfn "  Partition B: %A" solution.PartitionT
            printfn "  Cut Value: %.1f" solution.CutValue
            printfn "  Cut Edges: %d" solution.CutEdges.Length
            printfn "  Backend: %s" solution.BackendName
            for edge in solution.CutEdges do
                printfn "    %s <-> %s (weight: %.1f)" edge.Source edge.Target edge.Weight
            printfn ""
        Some (resultRow example vertices problem.EdgeCount solution)
    | Error err ->
        if not quiet then
            printfn "  Failed: %s" err.Message
            printfn ""
        None

let results = ResizeArray<Map<string, string>>()
let shouldRun name = exampleName = "all" || exampleName = name

// ---------------------------------------------------------------------------
// EXAMPLE 1: Small Circuit Design (4 blocks)
// ---------------------------------------------------------------------------

if shouldRun "circuit" then
    if not quiet then
        printfn "======================================"
        printfn "MaxCut - Circuit Wire Minimization"
        printfn "======================================"
        printfn ""
        printfn "Example 1: Small Circuit with 4 Blocks"
        printfn "--------------------------------------"

    let blocks = ["CPU"; "GPU"; "RAM"; "IO"]

    let interconnects = [
        ("CPU", "GPU", 5.0)
        ("CPU", "RAM", 10.0)
        ("CPU", "IO", 2.0)
        ("GPU", "RAM", 7.0)
        ("GPU", "IO", 1.0)
        ("RAM", "IO", 3.0)
    ]

    let circuitProblem = MaxCut.createProblem blocks interconnects

    if not quiet then
        printfn "Circuit Blocks: %A" blocks
        printfn "Interconnects: %d edges, total weight: %.1f"
            circuitProblem.EdgeCount
            (interconnects |> List.sumBy (fun (_, _, w) -> w))
        printfn ""
        printfn "Solving with quantum QAOA..."

    solveAndReport "circuit" blocks circuitProblem
    |> Option.iter results.Add

// ---------------------------------------------------------------------------
// EXAMPLE 2: Helper Functions - Common Graph Structures
// ---------------------------------------------------------------------------

if shouldRun "helpers" then
    if not quiet then
        printfn "Example 2: Helper Functions for Common Graphs"
        printfn "----------------------------------------------"

    // Complete graph K4
    if not quiet then printfn "Complete Graph (K4):"
    let k4 = MaxCut.completeGraph ["A"; "B"; "C"; "D"] 1.0
    if not quiet then
        printfn "  Vertices: %d, Edges: %d" k4.VertexCount k4.EdgeCount
    solveAndReport "helpers_k4" ["A"; "B"; "C"; "D"] k4
    |> Option.iter results.Add

    // Cycle graph C4
    if not quiet then printfn "Cycle Graph (C4):"
    let c4 = MaxCut.cycleGraph ["A"; "B"; "C"; "D"] 1.0
    if not quiet then
        printfn "  Vertices: %d, Edges: %d" c4.VertexCount c4.EdgeCount
    solveAndReport "helpers_c4" ["A"; "B"; "C"; "D"] c4
    |> Option.iter results.Add

    // Star graph
    if not quiet then printfn "Star Graph (1 center, 3 spokes):"
    let star = MaxCut.starGraph "Hub" ["S1"; "S2"; "S3"] 1.0
    if not quiet then
        printfn "  Vertices: %d, Edges: %d" star.VertexCount star.EdgeCount
    solveAndReport "helpers_star" ["Hub"; "S1"; "S2"; "S3"] star
    |> Option.iter results.Add

    // Grid graph 2x3
    if not quiet then printfn "Grid Graph (2x3):"
    let grid = MaxCut.gridGraph 2 3 1.0
    if not quiet then
        printfn "  Vertices: %d, Edges: %d" grid.VertexCount grid.EdgeCount
    // Grid vertices are generated internally; use a placeholder list with the right count
    let gridVertices = [ for i in 0 .. grid.VertexCount - 1 -> sprintf "(%d,%d)" (i / 3) (i % 3) ]
    solveAndReport "helpers_grid" gridVertices grid
    |> Option.iter results.Add

// ---------------------------------------------------------------------------
// EXAMPLE 3: Social Network Community Detection
// ---------------------------------------------------------------------------

if shouldRun "social" then
    if not quiet then
        printfn "Example 3: Social Network Community Detection"
        printfn "---------------------------------------------"

    let people = ["Alice"; "Bob"; "Charlie"; "David"; "Eve"; "Frank"]

    let socialNetwork = [
        ("Alice", "Bob", 5.0)
        ("Alice", "Charlie", 3.0)
        ("Bob", "Charlie", 4.0)
        ("David", "Eve", 6.0)
        ("David", "Frank", 5.0)
        ("Eve", "Frank", 4.0)
        ("Charlie", "David", 1.0)
    ]

    let networkProblem = MaxCut.createProblem people socialNetwork

    if not quiet then
        printfn "Social Network: %d people, %d connections"
            networkProblem.VertexCount networkProblem.EdgeCount
        printfn ""

    solveAndReport "social" people networkProblem
    |> Option.iter results.Add

// ---------------------------------------------------------------------------
// EXAMPLE 4: Simple Triangle Graph
// ---------------------------------------------------------------------------

if shouldRun "triangle" then
    if not quiet then
        printfn "Example 4: Simple Triangle Graph"
        printfn "---------------------------------"

    let vertices = ["X"; "Y"; "Z"]
    let edges = [("X", "Y", 2.0); ("Y", "Z", 3.0); ("Z", "X", 1.0)]
    let triangleProblem = MaxCut.createProblem vertices edges

    solveAndReport "triangle" vertices triangleProblem
    |> Option.iter results.Add

// ---------------------------------------------------------------------------
// EXAMPLE 5: Complete Graph K3
// ---------------------------------------------------------------------------

if shouldRun "k3" then
    if not quiet then
        printfn "Example 5: Complete Graph K3"
        printfn "-----------------------------"

    let k3 = MaxCut.completeGraph ["A"; "B"; "C"] 1.0

    if not quiet then
        printfn "  Complete graph K3: 3 vertices, 3 edges (all connected)"
        printfn "  For K3, optimal MaxCut = 2 (any 2 edges can be cut)"

    solveAndReport "k3" ["A"; "B"; "C"] k3
    |> Option.iter results.Add

// ---------------------------------------------------------------------------
// Custom graph from CSV input
// ---------------------------------------------------------------------------

match inputPath with
| Some path ->
    if not quiet then
        printfn "Custom Graph from: %s" path
        printfn "----------------------------"

    let scriptDir = __SOURCE_DIRECTORY__
    let resolved = Data.resolveRelative scriptDir path
    let rows = Data.readCsvWithHeader resolved

    let vertices =
        rows
        |> List.collect (fun r ->
            [ r.Values |> Map.tryFind "source" |> Option.defaultValue ""
              r.Values |> Map.tryFind "target" |> Option.defaultValue "" ])
        |> List.filter (fun s -> s <> "")
        |> List.distinct

    let edges =
        rows
        |> List.choose (fun r ->
            match Map.tryFind "source" r.Values, Map.tryFind "target" r.Values with
            | Some s, Some t ->
                let w =
                    r.Values
                    |> Map.tryFind "weight"
                    |> Option.bind (fun v ->
                        match Double.TryParse v with
                        | true, d -> Some d
                        | _ -> None)
                    |> Option.defaultValue 1.0
                Some (s, t, w)
            | _ -> None)

    if not quiet then
        printfn "  Loaded %d vertices, %d edges" vertices.Length edges.Length

    let customProblem = MaxCut.createProblem vertices edges
    solveAndReport "custom" vertices customProblem
    |> Option.iter results.Add

| None -> ()

// ---------------------------------------------------------------------------
// Structured output
// ---------------------------------------------------------------------------

let resultsList = results |> Seq.toList

match outputPath with
| Some p -> Reporting.writeJson p resultsList
| None -> ()

match csvPath with
| Some p ->
    let header = ["example"; "vertices"; "edges"; "cut_value"; "cut_edges"; "partition_s"; "partition_t"; "backend"]
    let rows =
        resultsList
        |> List.map (fun m ->
            header |> List.map (fun h -> m |> Map.tryFind h |> Option.defaultValue ""))
    Reporting.writeCsv p header rows
| None -> ()

if not quiet then
    printfn "======================================"
    printfn "MaxCut Examples Complete!"
    printfn "======================================"

if argv.Length = 0 && inputPath.IsNone then
    printfn ""
    printfn "Tip: run with --help for CLI options."
