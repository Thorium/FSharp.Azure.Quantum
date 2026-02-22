/// Graph Coloring Visualization Example
///
/// Demonstrates visualization of graph coloring solutions using
/// ASCII (terminal-friendly) and Mermaid (documentation/GitHub).
///
/// Use Case: Register Allocation for a Simple Compiler
/// - Variables x, y, z, w have conflicts when live at the same time
/// - Goal: assign CPU registers with minimal conflicts

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
    "GraphColoring-Visualization.fsx"
    "Visualize graph coloring solutions (register allocation) in ASCII and Mermaid."
    [ { Cli.OptionSpec.Name = "colors"
        Description = "Number of available colors/registers"
        Default = Some "4" }
      { Cli.OptionSpec.Name = "mermaid-file"
        Description = "Write Mermaid markdown to file"
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
// Problem definition: Register Allocation
// ---------------------------------------------------------------------------

let registerAllocation = graphColoring {
    node "x" ["y"; "z"]
    node "y" ["x"; "w"]
    node "z" ["x"; "w"]
    node "w" ["y"; "z"]
    colors ([ for i in 0 .. numColors - 1 -> sprintf "R%d" i ])
    objective MinimizeColors
}

pr "=== Graph Coloring Visualization ==="
pr "Use Case: Register Allocation"
pr ""

// ---------------------------------------------------------------------------
// Solve
// ---------------------------------------------------------------------------

match GraphColoring.solve registerAllocation numColors (Some quantumBackend) with
| Error err ->
    pr "[FAIL] %s" err.Message

| Ok solution ->
    pr "Solution found!"
    pr ""

    // ASCII visualization
    pr "--- ASCII Visualization (Terminal) ---"
    pr "%s" (solution.ToASCII())
    pr ""

    // Mermaid visualization
    let mermaidOutput = solution.ToMermaid()
    pr "--- Mermaid Diagram (GitHub/Docs) ---"
    pr "%s" mermaidOutput
    pr ""

    // Write Mermaid file if requested
    mermaidFile |> Option.iter (fun path ->
        let assignmentsText =
            solution.Assignments
            |> Map.toList
            |> List.map (fun (var, reg) -> sprintf "- `%s` -> `%s`" var reg)
            |> String.concat "\n"

        let markdown =
            sprintf "# Register Allocation Solution\n\n## Mermaid Diagram\n\n%s\n\n## Assignments\n%s\n\n## Analysis\n- Registers Used: %d / %d\n- Valid: %b\n- Cost: %.2f\n"
                mermaidOutput assignmentsText solution.ColorsUsed numColors solution.IsValid solution.Cost

        File.WriteAllText(path, markdown)
        pr "Mermaid markdown written to %s" path)

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
    pr "  dotnet fsi GraphColoring-Visualization.fsx -- --colors 3"
    pr "  dotnet fsi GraphColoring-Visualization.fsx -- --mermaid-file register-allocation.md"
    pr "  dotnet fsi GraphColoring-Visualization.fsx -- --quiet --output results.json --csv results.csv"
    pr "  dotnet fsi GraphColoring-Visualization.fsx -- --help"
