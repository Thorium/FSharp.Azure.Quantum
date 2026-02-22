/// Graph Coloring - Problem AND Solution Visualization
///
/// Demonstrates visualization at BOTH stages:
/// 1. BEFORE solving: Visualize the problem structure (graph, conflicts)
/// 2. AFTER solving: Visualize the solution (color assignments)
///
/// Key insight: .ToMermaid() and .ToASCII() work on BOTH
/// GraphColoringProblem AND ColoringSolution types.

//#r "nuget: FSharp.Azure.Quantum"
#r "nuget: Microsoft.Extensions.Logging.Abstractions, 10.0.0"
#r "../../src/FSharp.Azure.Quantum/bin/Debug/net10.0/FSharp.Azure.Quantum.dll"

#load "../_common/Cli.fs"
#load "../_common/Data.fs"
#load "../_common/Reporting.fs"

open System.IO
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.GraphColoring
open FSharp.Azure.Quantum.Visualization
open FSharp.Azure.Quantum.Backends.LocalBackend
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Examples.Common

// ---------------------------------------------------------------------------
// CLI
// ---------------------------------------------------------------------------

let argv = fsi.CommandLineArgs |> Array.skip 1
let args = Cli.parse argv

Cli.exitIfHelp
    "ProblemAndSolutionVisualization.fsx"
    "Visualize both the problem graph and the solved coloring side by side."
    [ { Cli.OptionSpec.Name = "colors"
        Description = "Number of available colors"
        Default = Some "4" }
      { Cli.OptionSpec.Name = "mermaid-file"
        Description = "Write combined Mermaid markdown to file"
        Default = None }
      { Cli.OptionSpec.Name = "output"
        Description = "Write results to JSON file"
        Default = None }
      { Cli.OptionSpec.Name = "csv"
        Description = "Write results to CSV file"
        Default = None }
      { Cli.OptionSpec.Name = "quiet"
        Description = "Suppress console output"
        Default = None } ]
    args

let quiet = Cli.hasFlag "quiet" args
let pr fmt = Printf.ksprintf (fun s -> if not quiet then printfn "%s" s) fmt

let numColors = Cli.getIntOr "colors" 4 args
let mermaidFile = Cli.tryGet "mermaid-file" args
let outputPath = Cli.tryGet "output" args
let csvPath = Cli.tryGet "csv" args

// ---------------------------------------------------------------------------
// Quantum backend (Rule 1: IQuantumBackend dependency)
// ---------------------------------------------------------------------------

let quantumBackend = LocalBackend() :> IQuantumBackend

// ---------------------------------------------------------------------------
// Problem definition
// ---------------------------------------------------------------------------

let registerAllocation = graphColoring {
    node "x" ["y"; "z"]
    node "y" ["x"; "w"]
    node "z" ["x"; "w"]
    node "w" ["y"; "z"]
    colors ([ for i in 0 .. numColors - 1 -> sprintf "R%d" i ])
    objective MinimizeColors
}

pr "=== Graph Coloring: Problem + Solution Visualization ==="
pr ""

// ---------------------------------------------------------------------------
// Step 1: Visualize the PROBLEM (before solving)
// ---------------------------------------------------------------------------

pr "--- Step 1: Problem Visualization ---"
pr ""
pr "ASCII (Terminal):"
pr "%s" (registerAllocation.ToASCII())
pr ""
pr "Mermaid (GitHub/Docs):"
pr "%s" (registerAllocation.ToMermaid())
pr ""

// ---------------------------------------------------------------------------
// Step 2: Solve
// ---------------------------------------------------------------------------

pr "--- Step 2: Solving... ---"
pr ""

match GraphColoring.solve registerAllocation numColors (Some quantumBackend) with
| Error err ->
    pr "[FAIL] %s" err.Message

| Ok solution ->
    pr "Solution found!"
    pr ""

    // Step 3: Visualize the SOLUTION
    pr "--- Step 3: Solution Visualization ---"
    pr ""
    pr "ASCII (Terminal):"
    pr "%s" (solution.ToASCII())
    pr ""
    pr "Mermaid (GitHub/Docs):"
    pr "%s" (solution.ToMermaid())
    pr ""

    // Write combined Mermaid doc if requested
    mermaidFile |> Option.iter (fun path ->
        let doc =
            sprintf "# Register Allocation\n\n## Problem Definition\n\n%s\n\n%s\n\n## Solution\n\n%s\n\n%s\n"
                (registerAllocation.ToASCII())
                (registerAllocation.ToMermaid())
                (solution.ToASCII())
                (solution.ToMermaid())
        File.WriteAllText(path, doc)
        pr "Documentation written to %s" path)

    // JSON output
    outputPath |> Option.iter (fun path ->
        let assignments =
            solution.Assignments |> Map.toList |> List.map (fun (v, c) -> sprintf "%s=%s" v c) |> String.concat ";"
        let payload =
            {| colorsUsed = solution.ColorsUsed
               colorsAvailable = numColors
               isValid = solution.IsValid
               cost = solution.Cost
               assignments = assignments |}
        Reporting.writeJson path payload
        pr "JSON written to %s" path)

    // CSV output
    csvPath |> Option.iter (fun path ->
        let header = [ "variable"; "register" ]
        let rows =
            solution.Assignments
            |> Map.toList
            |> List.map (fun (v, c) -> [ v; c ])
        Reporting.writeCsv path header rows
        pr "CSV written to %s" path)

// ---------------------------------------------------------------------------
// Usage hints
// ---------------------------------------------------------------------------

if not quiet && outputPath.IsNone && csvPath.IsNone then
    pr ""
    pr "Tip: run with arguments for custom parameters:"
    pr "  dotnet fsi ProblemAndSolutionVisualization.fsx -- --colors 3"
    pr "  dotnet fsi ProblemAndSolutionVisualization.fsx -- --mermaid-file problem-and-solution.md"
    pr "  dotnet fsi ProblemAndSolutionVisualization.fsx -- --quiet --output results.json --csv results.csv"
    pr "  dotnet fsi ProblemAndSolutionVisualization.fsx -- --help"
