// ============================================================================
// ProblemDecomposition Example
// ============================================================================
// Demonstrates the generic problem decomposition orchestrator that
// automatically splits large problems into sub-problems when the backend
// has limited qubit capacity (IQubitLimitedBackend).
//
// Features demonstrated:
//   1. connectedComponents     - Find connected components in a graph
//   2. partitionByComponents   - Split graph into independent sub-graphs
//   3. canDecomposeWithinLimit - Check if a graph can be decomposed to fit
//   4. plan + execute          - Two-phase decomposition workflow
//   5. solveWithDecomposition  - One-shot convenience function
//
// Usage:
//   dotnet fsi ProblemDecomposition_Example.fsx
//   dotnet fsi ProblemDecomposition_Example.fsx -- --example components
//   dotnet fsi ProblemDecomposition_Example.fsx -- --quiet --output results.json
// ============================================================================

#r "../../src/FSharp.Azure.Quantum/bin/Debug/net10.0/FSharp.Azure.Quantum.dll"
#load "../_common/Cli.fs"
#load "../_common/Data.fs"
#load "../_common/Reporting.fs"
open FSharp.Azure.Quantum.Examples.Common

open System
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Backends.LocalBackend

// --- CLI ---
let argv = fsi.CommandLineArgs |> Array.skip 1
let args = Cli.parse argv
Cli.exitIfHelp "ProblemDecomposition_Example.fsx" "Problem decomposition for qubit-limited backends" [
    { Name = "example"; Description = "Which example: all, components, partition, plan, solve"; Default = Some "all" }
    { Name = "output"; Description = "Write results to JSON file"; Default = None }
    { Name = "csv"; Description = "Write results to CSV file"; Default = None }
    { Name = "quiet"; Description = "Suppress printed output"; Default = None }
] args

let exampleName = Cli.getOr "example" "all" args
let quiet = Cli.hasFlag "quiet" args
let outputPath = Cli.tryGet "output" args
let csvPath = Cli.tryGet "csv" args

let pr fmt = Printf.ksprintf (fun s -> if not quiet then printfn "%s" s) fmt

let runAll = (exampleName = "all")

// Accumulate results for JSON/CSV export
let mutable jsonResults : obj list = []
let mutable csvRows : string list list = []

// --- Quantum Backend (Rule 1) ---
let quantumBackend = LocalBackend() :> IQuantumBackend

// ============================================================================
// Example 1: Connected components
// ============================================================================
// Find connected components in a graph with 8 vertices and 3 components

if runAll || exampleName = "components" then
    pr "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
    pr " Example 1: Connected Components"
    pr " Find independent sub-graphs in an 8-vertex graph."
    pr "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
    pr ""
    pr "  Graph: 0--1--2   3--4   5--6--7"
    pr ""

    let numVertices = 8
    let edges = [ (0, 1); (1, 2); (3, 4); (5, 6); (6, 7) ]

    let components = ProblemDecomposition.connectedComponents numVertices edges

    pr "  Found %d connected components:" components.Length
    for i, comp in components |> List.indexed do
        let verticesStr = comp |> List.sort |> List.map string |> String.concat ", "
        pr "    Component %d: { %s } (%d vertices)" (i + 1) verticesStr comp.Length
        jsonResults <- (box {| Example = "Components"; Component = i + 1; Vertices = verticesStr; Size = comp.Length |}) :: jsonResults
        csvRows <- [ "Components"; string (i + 1); verticesStr; string comp.Length ] :: csvRows

// ============================================================================
// Example 2: Partition by components
// ============================================================================

if runAll || exampleName = "partition" then
    pr ""
    pr "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
    pr " Example 2: Partition by Components"
    pr " Split graph into sub-graphs with their internal edges."
    pr "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"

    let numVertices = 8
    let edges = [ (0, 1); (1, 2); (3, 4); (5, 6); (6, 7) ]

    let partitions = ProblemDecomposition.partitionByComponents numVertices edges

    pr ""
    pr "  %-12s  %-20s  %s" "Partition" "Vertices" "Edges"
    pr "  %-12s  %-20s  %s" "------------" "--------------------" "--------------------"

    for i, (verts, subEdges) in partitions |> List.indexed do
        let vertStr = verts |> List.sort |> List.map string |> String.concat ","
        let edgeStr = subEdges |> List.map (fun (a, b) -> sprintf "%d-%d" a b) |> String.concat ", "
        pr "  Partition %-2d  { %-17s}  [ %s ]" (i + 1) vertStr edgeStr
        jsonResults <- (box {| Example = "Partition"; Index = i + 1; Vertices = vertStr; Edges = edgeStr |}) :: jsonResults
        csvRows <- [ "Partition"; string (i + 1); vertStr; edgeStr ] :: csvRows

    // Check if it fits within qubit limit
    let maxQubits = 4
    let canFit = ProblemDecomposition.canDecomposeWithinLimit numVertices edges maxQubits 1
    pr ""
    pr "  Can decompose to fit %d-qubit backend (1 qubit/vertex): %b" maxQubits canFit

// ============================================================================
// Example 3: Two-phase plan + execute
// ============================================================================
// Show the plan/execute workflow with a simple problem type

if runAll || exampleName = "plan" then
    pr ""
    pr "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
    pr " Example 3: Plan + Execute Workflow"
    pr " Use the two-phase API to plan decomposition, then execute."
    pr "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"

    // A simple problem: sum of N numbers, decomposable by splitting the list
    type SumProblem = { Numbers: int list }
    type SumSolution = { Total: int }

    let estimateQubits (p: SumProblem) = p.Numbers.Length
    let decompose (p: SumProblem) =
        // Split into halves
        let mid = p.Numbers.Length / 2
        [ { Numbers = p.Numbers.[.. mid - 1] }
          { Numbers = p.Numbers.[mid ..] } ]
    let recombine (sols: SumSolution list) =
        { Total = sols |> List.sumBy (fun s -> s.Total) }
    let solveFn (p: SumProblem) : Result<SumSolution, QuantumError> =
        Ok { Total = p.Numbers |> List.sum }

    let problem = { Numbers = [ 10; 20; 30; 40; 50; 60 ] }

    // Plan phase
    let strategy = ProblemDecomposition.FixedPartition 4
    let decompositionPlan = ProblemDecomposition.plan strategy quantumBackend estimateQubits decompose problem

    match decompositionPlan with
    | ProblemDecomposition.RunDirect _ ->
        pr "  Plan: Run directly (problem fits in backend)"
    | ProblemDecomposition.RunDecomposed subProblems ->
        pr "  Plan: Decompose into %d sub-problems" subProblems.Length
        for i, sub in subProblems |> List.indexed do
            pr "    Sub-problem %d: %A (%d qubits)" (i + 1) sub.Numbers (estimateQubits sub)

    // Execute phase
    let result = ProblemDecomposition.execute solveFn recombine decompositionPlan

    match result with
    | Ok sol ->
        pr ""
        pr "  Result: Total = %d (expected %d)" sol.Total (problem.Numbers |> List.sum)
        jsonResults <- (box {| Example = "PlanExecute"; Total = sol.Total; Decomposed = true |}) :: jsonResults
        csvRows <- [ "PlanExecute"; string sol.Total; "true"; "" ] :: csvRows
    | Error e ->
        pr "  FAILED: %A" e

// ============================================================================
// Example 4: One-shot solveWithDecomposition
// ============================================================================

if runAll || exampleName = "solve" then
    pr ""
    pr "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
    pr " Example 4: One-Shot solveWithDecomposition"
    pr " Convenience function that plans and executes in one call."
    pr "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"

    type ArrayProblem = { Values: float list }
    type ArraySolution = { Sum: float; Count: int }

    let problem = { Values = [ 1.5; 2.5; 3.0; 4.0; 5.5; 6.0; 7.5; 8.0 ] }

    let result =
        ProblemDecomposition.solveWithDecomposition
            quantumBackend
            problem
            (fun p -> p.Values.Length)
            (fun p ->
                let mid = p.Values.Length / 2
                [ { Values = p.Values.[.. mid - 1] }
                  { Values = p.Values.[mid ..] } ])
            (fun sols ->
                { Sum = sols |> List.sumBy (fun s -> s.Sum)
                  Count = sols |> List.sumBy (fun s -> s.Count) })
            (fun p ->
                Ok { Sum = p.Values |> List.sum; Count = p.Values.Length })

    match result with
    | Ok sol ->
        pr "  Sum:    %.1f (expected %.1f)" sol.Sum (problem.Values |> List.sum)
        pr "  Count:  %d (expected %d)" sol.Count problem.Values.Length
        jsonResults <- (box {| Example = "SolveWithDecomp"; Sum = sol.Sum; Count = sol.Count |}) :: jsonResults
        csvRows <- [ "SolveWithDecomp"; sprintf "%.1f" sol.Sum; string sol.Count; "" ] :: csvRows
    | Error e ->
        pr "  FAILED: %A" e

// --- JSON output ---
outputPath |> Option.iter (fun path ->
    Reporting.writeJson path (jsonResults |> List.rev)
    pr "JSON written to %s" path
)

// --- CSV output ---
csvPath |> Option.iter (fun path ->
    let header = [ "Example"; "Value1"; "Value2"; "Extra" ]
    Reporting.writeCsv path header (csvRows |> List.rev)
    pr "CSV written to %s" path
)

// --- Usage hints ---
if not quiet && outputPath.IsNone && csvPath.IsNone then
    pr "-------------------------------------------"
    pr "Tip: Use --example components|partition|plan|solve to run one."
    pr "     Use --output results.json or --csv results.csv to export."
    pr "     Use --help for all options."
