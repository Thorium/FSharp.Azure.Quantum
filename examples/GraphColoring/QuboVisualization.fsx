/// QUBO Matrix Visualization
///
/// Demonstrates how to visualize the QUBO (Quadratic Unconstrained Binary
/// Optimization) matrix that represents the internal mathematical formulation
/// of graph coloring problems.
///
/// What is QUBO?
/// - Converts discrete optimization problems into quadratic form
/// - Used by quantum annealers (D-Wave) and QAOA variational circuits
/// - Matrix coefficients encode both objectives and constraints
///
/// Why visualize QUBO?
/// - Debug problem formulations
/// - Understand variable interactions
/// - Verify constraint encoding
/// - Educational: see how graph coloring maps to binary optimization

//#r "nuget: FSharp.Azure.Quantum"
#r "../../src/FSharp.Azure.Quantum/bin/Debug/net10.0/FSharp.Azure.Quantum.dll"

#load "../_common/Cli.fs"
#load "../_common/Data.fs"
#load "../_common/Reporting.fs"

open System
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
    "QuboVisualization.fsx"
    "Visualize the QUBO matrix behind a graph coloring problem (variable mapping, coefficients)."
    [ { Cli.OptionSpec.Name = "colors"
        Description = "Number of available colors"
        Default = Some "2" }
      { Cli.OptionSpec.Name = "penalty"
        Description = "QUBO constraint penalty weight"
        Default = Some "10.0" }
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

let numColors = Cli.getIntOr "colors" 2 args
let penaltyWeight = Cli.getFloatOr "penalty" 10.0 args
let outputPath = Cli.tryGet "output" args
let csvPath = Cli.tryGet "csv" args

// ---------------------------------------------------------------------------
// Quantum backend (Rule 1: IQuantumBackend dependency)
// Available for downstream quantum execution of the QUBO via QAOA
// ---------------------------------------------------------------------------

let quantumBackend = LocalBackend() :> IQuantumBackend

// ---------------------------------------------------------------------------
// Problem definition (high-level CE for display)
// ---------------------------------------------------------------------------

let colorNames = [ for i in 0 .. numColors - 1 -> sprintf "Color%d" i ]

let coloringProblem = graphColoring {
    node "A" ["B"; "C"]
    node "B" ["A"]
    node "C" ["A"]
    colors colorNames
    objective MinimizeColors
}

pr "=== QUBO Matrix Visualization ==="
pr ""

// Step 1: Show original problem
pr "Step 1: Original Problem"
pr "------------------------"
pr "%s" (coloringProblem.ToASCII())

// ---------------------------------------------------------------------------
// Build low-level quantum solver problem for QUBO conversion
// ---------------------------------------------------------------------------

let quantumProblem : Quantum.QuantumGraphColoringSolver.GraphColoringProblem = {
    Vertices = ["A"; "B"; "C"]
    Edges = [
        GraphOptimization.edge "A" "B" 1.0 |> fun e -> { e with Value = Some () }
        GraphOptimization.edge "A" "C" 1.0 |> fun e -> { e with Value = Some () }
    ]
    NumColors = numColors
    FixedColors = Map.empty
}

// ---------------------------------------------------------------------------
// Generate and display QUBO matrix
// ---------------------------------------------------------------------------

match Quantum.QuantumGraphColoringSolver.toQubo quantumProblem penaltyWeight with
| Error err ->
    pr "[FAIL] Error generating QUBO: %A" err

| Ok (quboMatrix, variableMap) ->
    // Step 2: Variable mapping
    pr "Step 2: Variable Mapping"
    pr "------------------------"
    pr "QUBO uses binary variables to encode color assignments:"
    pr ""
    variableMap
    |> Map.toList
    |> List.sortBy fst
    |> List.iter (fun (idx, (vertex, color)) ->
        pr "  x_%d = 1  means  vertex '%s' has color %d" idx vertex color)
    pr ""

    // Step 3: QUBO matrix ASCII
    pr "Step 3: QUBO Matrix (ASCII)"
    pr "---------------------------"
    pr "%s" (quboMatrix.ToASCII())

    // Step 4: QUBO matrix Mermaid
    pr "Step 4: QUBO Matrix (Mermaid)"
    pr "-----------------------------"
    pr "%s" (quboMatrix.ToMermaid())

    // Step 5: Interpretation guide
    pr "--- QUBO Interpretation Guide ---"
    pr ""
    pr "Linear terms (diagonal Q[i,i]):"
    pr "  Positive -> penalizes x_i = 1"
    pr "  Negative -> rewards x_i = 1"
    pr ""
    pr "Quadratic terms (off-diagonal Q[i,j]):"
    pr "  Positive -> penalizes x_i = x_j = 1 simultaneously"
    pr "  Negative -> rewards x_i = x_j = 1 simultaneously"
    pr ""
    pr "In graph coloring QUBO:"
    pr "  1. Large positive off-diagonal: penalty for adjacent nodes with same color"
    pr "  2. One-hot constraints: each node gets exactly ONE color"
    pr "  3. Penalty weight: %.1f (controls constraint enforcement strength)" penaltyWeight
    pr ""

    // JSON output
    outputPath |> Option.iter (fun path ->
        let coefficients =
            quboMatrix.Q
            |> Map.toList
            |> List.map (fun ((i, j), v) -> sprintf "(%d,%d)=%.4f" i j v)
            |> String.concat ";"
        let variables =
            variableMap
            |> Map.toList
            |> List.sortBy fst
            |> List.map (fun (idx, (vertex, color)) -> sprintf "x%d=%s:%d" idx vertex color)
            |> String.concat ";"
        let payload =
            {| numVariables = quboMatrix.NumVariables
               numCoefficients = quboMatrix.Q.Count
               penaltyWeight = penaltyWeight
               numColors = numColors
               variables = variables
               coefficients = coefficients |}
        Reporting.writeJson path payload
        pr "JSON written to %s" path)

    // CSV output
    csvPath |> Option.iter (fun path ->
        let header = [ "row"; "col"; "coefficient"; "type" ]
        let rows =
            quboMatrix.Q
            |> Map.toList
            |> List.sortBy fst
            |> List.map (fun ((i, j), v) ->
                [ string i; string j; sprintf "%.4f" v; if i = j then "linear" else "quadratic" ])
        Reporting.writeCsv path header rows
        pr "CSV written to %s" path)

// ---------------------------------------------------------------------------
// Usage hints
// ---------------------------------------------------------------------------

if not quiet && outputPath.IsNone && csvPath.IsNone then
    pr ""
    pr "Tip: run with arguments for custom parameters:"
    pr "  dotnet fsi QuboVisualization.fsx -- --colors 3 --penalty 20.0"
    pr "  dotnet fsi QuboVisualization.fsx -- --quiet --output results.json --csv results.csv"
    pr "  dotnet fsi QuboVisualization.fsx -- --help"
