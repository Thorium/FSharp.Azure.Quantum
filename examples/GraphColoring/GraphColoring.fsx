// ============================================================================
// Graph Coloring Examples - FSharp.Azure.Quantum
// ============================================================================
//
// This script demonstrates the quantum-first Graph Coloring API with multiple
// real-world use cases:
//
// 1. Register Allocation (compiler optimization)
// 2. Frequency Assignment (wireless network planning)
// 3. Exam Scheduling (university timetabling)
// 4. Simple Graph Coloring (basic K-coloring)
// 5. Comparison: Quantum vs Classical
//
// WHAT IS GRAPH COLORING:
// Assign colors to graph vertices such that no adjacent vertices share
// the same color, while minimizing the total number of colors used.
//
// WHY USE QUANTUM:
// - Quantum QAOA can explore color assignments in superposition
// - Better solutions for complex constraint graphs
// - Useful for NP-complete problems (chromatic number)
//
// ============================================================================

(*
===============================================================================
 Background Theory
===============================================================================

Graph k-coloring asks: can a graph G = (V, E) be colored with at most k colors
such that no two adjacent vertices share a color? Determining the chromatic
number Ï‡(G) (minimum k for which this is possible) is NP-hard. Graph coloring
has extensive applications: register allocation in compilers (interference graph),
frequency assignment in wireless networks (avoid interference), exam scheduling
(avoid student conflicts), map coloring, and Sudoku solving.

The problem is formulated as QUBO by introducing binary variables xáµ¥,c âˆˆ {0,1}
indicating vertex v has color c. Constraints ensure: (1) each vertex gets exactly
one color: Î£c xáµ¥,c = 1, and (2) adjacent vertices differ: xáµ¤,c + xáµ¥,c â‰¤ 1 for
edges (u,v). These constraints are converted to penalty terms in the objective
function, which QAOA minimizes. Finding the chromatic number requires testing
k = 1, 2, ... until a valid coloring exists.

Key Equations:
  - One-color constraint: (Î£c xáµ¥,c - 1)Â² = 0 for each vertex v
  - Edge constraint: Î£c xáµ¤,cÂ·xáµ¥,c = 0 for each edge (u,v)
  - QUBO objective: min Î£áµ¥ A(Î£c xáµ¥,c - 1)Â² + Î£â‚áµ¤,áµ¥â‚ŽâˆˆE BÂ·Î£c xáµ¤,cÂ·xáµ¥,c
  - Chromatic number: Ï‡(G) = min{k : G is k-colorable}
  - Greedy upper bound: Ï‡(G) â‰¤ Î”(G) + 1 where Î” is max degree

Quantum Advantage:
  Graph coloring's combinatorial explosion (kâ¿ possible assignments for n vertices,
  k colors) makes it ideal for quantum speedup. QAOA explores colorings in
  superposition, with interference amplifying valid solutions. For sparse graphs
  with complex constraint structures (e.g., register allocation interference
  graphs), quantum approaches may find optimal or near-optimal colorings faster
  than classical branch-and-bound. Current demonstrations handle ~20 vertices;
  scaling to industrial compiler workloads requires fault-tolerant hardware.

References:
  [1] Garey & Johnson, "Computers and Intractability: A Guide to the Theory of
      NP-Completeness", W.H. Freeman (1979), Section 5.5.
  [2] Lucas, "Ising formulations of many NP problems", Front. Phys. 2, 5 (2014).
      https://doi.org/10.3389/fphy.2014.00005
  [3] Tabi et al., "Quantum Optimization for the Graph Coloring Problem with
      Polynomial Encoding", IEEE ICRC (2020). https://doi.org/10.1109/ICRC2020.2020.00006
  [4] Wikipedia: Graph_coloring
      https://en.wikipedia.org/wiki/Graph_coloring
*)

//#r "nuget: FSharp.Azure.Quantum"
#r "nuget: Microsoft.Extensions.Logging.Abstractions, 10.0.0"
#r "../../src/FSharp.Azure.Quantum/bin/Debug/net10.0/FSharp.Azure.Quantum.dll"

#load "../_common/Cli.fs"
#load "../_common/Data.fs"
#load "../_common/Reporting.fs"

open System
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.GraphColoring
open FSharp.Azure.Quantum.Examples.Common

// ---------------------------------------------------------------------------
// CLI
// ---------------------------------------------------------------------------

let argv = fsi.CommandLineArgs |> Array.skip 1
let args = Cli.parse argv

Cli.exitIfHelp
    "GraphColoring.fsx"
    "Solve graph coloring problems using quantum QAOA optimization."
    [ { Cli.OptionSpec.Name = "example"
        Description = "Example to run: registers|frequency|exams|cycle|dense|precolored|all"
        Default = Some "all" }
      { Cli.OptionSpec.Name = "colors"
        Description = "Number of colors to try (where applicable)"
        Default = Some "3" }
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
let _numColors = Cli.getIntOr "colors" 3 args
let outputPath = Cli.tryGet "output" args
let csvPath = Cli.tryGet "csv" args

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

/// Build a structured result row from a graph coloring solution.
let resultRow (example: string) (solution: GraphColoring.ColoringSolution) : Map<string, string> =
    let assignments =
        solution.Assignments
        |> Map.toList
        |> List.map (fun (k, v) -> sprintf "%s=%s" k v)
        |> String.concat ";"
    [ "example", example
      "colors_used", string solution.ColorsUsed
      "conflicts", string solution.ConflictCount
      "valid", string solution.IsValid
      "assignments", assignments
      "backend", solution.BackendName ]
    |> Map.ofList

/// Solve a graph coloring problem, print results, and return a result row on success.
let solveAndReport
    (example: string)
    (problem: GraphColoring.GraphColoringProblem)
    (numColors: int)
    : Map<string, string> option =
    match GraphColoring.solve problem numColors None with
    | Ok solution ->
        if not quiet then
            printfn "  Colors used: %d" solution.ColorsUsed
            printfn "  Valid: %b | Conflicts: %d" solution.IsValid solution.ConflictCount
            printfn ""
            printfn "  Assignments:"
            for (node, color) in Map.toList solution.Assignments do
                printfn "    %s -> %s" node color
            printfn ""
            printfn "  Color Distribution:"
            for (color, count) in Map.toList solution.ColorDistribution do
                printfn "    %s: %d vertices" color count
            printfn ""
        Some (resultRow example solution)
    | Error err ->
        if not quiet then
            printfn "  Error: %s" err.Message
            printfn ""
        None

let results = ResizeArray<Map<string, string>>()
let shouldRun name = exampleName = "all" || exampleName = name

// ---------------------------------------------------------------------------
// EXAMPLE 1: Register Allocation (Compiler Optimization)
// ---------------------------------------------------------------------------

if shouldRun "registers" then
    if not quiet then
        printfn "========================================="
        printfn "EXAMPLE 1: Register Allocation"
        printfn "========================================="
        printfn ""
        printfn "Problem: Allocate 4 variables to CPU registers"
        printfn "Conflicts: R1<->R2, R1<->R3, R2<->R4, R3<->R4"
        printfn ""

    let registerProblem = graphColoring {
        node "R1" ["R2"; "R3"]
        node "R2" ["R1"; "R4"]
        node "R3" ["R1"; "R4"]
        node "R4" ["R2"; "R3"]
        colors ["EAX"; "EBX"; "ECX"; "EDX"]
        objective MinimizeColors
    }

    solveAndReport "registers" registerProblem 4
    |> Option.iter results.Add

// ---------------------------------------------------------------------------
// EXAMPLE 2: Frequency Assignment (Wireless Network Planning)
// ---------------------------------------------------------------------------

if shouldRun "frequency" then
    if not quiet then
        printfn "========================================="
        printfn "EXAMPLE 2: Frequency Assignment"
        printfn "========================================="
        printfn ""
        printfn "Problem: Assign frequencies to 3 cell towers"
        printfn "Interference pairs: 3 edges (complete triangle)"
        printfn ""

    let frequencyProblem = graphColoring {
        node "Tower1" ["Tower2"; "Tower3"]
        node "Tower2" ["Tower1"; "Tower3"]
        node "Tower3" ["Tower1"; "Tower2"]
        colors ["F1"; "F2"; "F3"]
        objective MinimizeColors
    }

    solveAndReport "frequency" frequencyProblem 3
    |> Option.iter results.Add

// ---------------------------------------------------------------------------
// EXAMPLE 3: Exam Scheduling (University Timetabling)
// ---------------------------------------------------------------------------

if shouldRun "exams" then
    if not quiet then
        printfn "========================================="
        printfn "EXAMPLE 3: Exam Scheduling"
        printfn "========================================="
        printfn ""
        printfn "Problem: Schedule 3 exams into time slots"
        printfn "Student conflicts: 2 pairs"
        printfn ""

    let examProblem = graphColoring {
        node "Math101" ["CS101"; "Physics101"]
        node "CS101" ["Math101"]
        node "Physics101" ["Math101"]
        colors ["Morning"; "Afternoon"; "Evening"]
        objective MinimizeColors
    }

    solveAndReport "exams" examProblem 3
    |> Option.iter results.Add

// ---------------------------------------------------------------------------
// EXAMPLE 4: Cycle Graph Coloring
// ---------------------------------------------------------------------------

if shouldRun "cycle" then
    if not quiet then
        printfn "========================================="
        printfn "EXAMPLE 4: Cycle Graph Coloring"
        printfn "========================================="
        printfn ""
        printfn "Problem: Color cycle graph (4 vertices, 4 edges)"
        printfn "Known chromatic number: 2 colors"
        printfn ""

    let cycleGraph = graphColoring {
        node "V1" ["V2"; "V4"]
        node "V2" ["V1"; "V3"]
        node "V3" ["V2"; "V4"]
        node "V4" ["V3"; "V1"]
        colors ["Red"; "Green"; "Blue"]
        objective MinimizeColors
    }

    solveAndReport "cycle" cycleGraph 3
    |> Option.iter results.Add

// ---------------------------------------------------------------------------
// EXAMPLE 5: Complex Dense Graph
// ---------------------------------------------------------------------------

if shouldRun "dense" then
    if not quiet then
        printfn "========================================="
        printfn "EXAMPLE 5: Complex Dense Graph"
        printfn "========================================="
        printfn ""
        printfn "Problem: Color 4-vertex graph with dense edges"
        printfn ""

    let comparisonGraph = graphColoring {
        node "A" ["B"; "C"]
        node "B" ["A"; "C"; "D"]
        node "C" ["A"; "B"; "D"]
        node "D" ["B"; "C"]
        colors ["Color1"; "Color2"; "Color3"]
        objective MinimizeColors
    }

    solveAndReport "dense" comparisonGraph 3
    |> Option.iter results.Add

// ---------------------------------------------------------------------------
// EXAMPLE 6: Pre-colored Vertices (Fixed Colors)
// ---------------------------------------------------------------------------

if shouldRun "precolored" then
    if not quiet then
        printfn "========================================="
        printfn "EXAMPLE 6: Pre-colored Vertices"
        printfn "========================================="
        printfn ""
        printfn "Problem: R1 is pre-assigned to EAX, color the rest"
        printfn ""

    let precoloredProblem = graphColoring {
        nodes [
            coloredNode {
                nodeId "R1"
                conflictsWith ["R2"; "R3"]
                fixedColor "EAX"
            }
        ]
        node "R2" ["R1"; "R4"]
        node "R3" ["R1"; "R4"]
        node "R4" ["R2"; "R3"]
        colors ["EAX"; "EBX"; "ECX"; "EDX"]
        objective MinimizeColors
    }

    match GraphColoring.solve precoloredProblem 4 None with
    | Ok solution ->
        if not quiet then
            printfn "  Colors used: %d" solution.ColorsUsed
            printfn "  Valid: %b | Conflicts: %d" solution.IsValid solution.ConflictCount
            printfn ""
            printfn "  Assignments:"
            for (var, register) in Map.toList solution.Assignments do
                let marker = if var = "R1" then " (fixed)" else ""
                printfn "    %s -> %s%s" var register marker
            printfn ""
        results.Add(resultRow "precolored" solution)
    | Error err ->
        if not quiet then
            printfn "  Error: %s" err.Message
            printfn ""

// ---------------------------------------------------------------------------
// Structured output
// ---------------------------------------------------------------------------

let resultsList = results |> Seq.toList

match outputPath with
| Some p -> Reporting.writeJson p resultsList
| None -> ()

match csvPath with
| Some p ->
    let header = ["example"; "colors_used"; "conflicts"; "valid"; "assignments"; "backend"]
    let rows =
        resultsList
        |> List.map (fun m ->
            header |> List.map (fun h -> m |> Map.tryFind h |> Option.defaultValue ""))
    Reporting.writeCsv p header rows
| None -> ()

if not quiet then
    printfn "========================================="
    printfn "Graph Coloring Examples Complete!"
    printfn "========================================="

if argv.Length = 0 then
    printfn ""
    printfn "Tip: run with --help for CLI options."
