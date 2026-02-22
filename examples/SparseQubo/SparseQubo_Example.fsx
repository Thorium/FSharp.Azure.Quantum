// ============================================================================
// Sparse QUBO Pipeline Example
// ============================================================================
// Demonstrates the sparse QUBO execution pipeline and budget-constrained
// execution from QaoaExecutionHelpers. The sparse pipeline uses Map<int*int,float>
// instead of dense float[,] arrays, providing better memory efficiency for
// problems where most qubit pairs have zero interaction.
//
// Features demonstrated:
//   1. evaluateQuboSparse    - Evaluate a QUBO cost for a given bitstring
//   2. executeQaoaCircuitSparse - Run QAOA with sparse QUBO representation
//   3. executeQaoaWithGridSearchSparse - Grid search over QAOA parameters
//   4. executeQaoaWithOptimizationSparse - Nelder-Mead optimization
//   5. executeWithBudget     - Budget-constrained execution with decomposition
//
// Usage:
//   dotnet fsi SparseQubo_Example.fsx
//   dotnet fsi SparseQubo_Example.fsx -- --example budget
//   dotnet fsi SparseQubo_Example.fsx -- --shots 2000 --quiet
//   dotnet fsi SparseQubo_Example.fsx -- --output results.json
// ============================================================================

#r "nuget: Microsoft.Extensions.Logging.Abstractions, 10.0.0"
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
Cli.exitIfHelp "SparseQubo_Example.fsx" "Sparse QUBO pipeline and budget-constrained QAOA execution" [
    { Name = "example"; Description = "Which example: all, evaluate, gridsearch, optimize, budget"; Default = Some "all" }
    { Name = "shots"; Description = "Number of measurement shots"; Default = Some "500" }
    { Name = "output"; Description = "Write results to JSON file"; Default = None }
    { Name = "csv"; Description = "Write results to CSV file"; Default = None }
    { Name = "quiet"; Description = "Suppress printed output"; Default = None }
] args

let exampleName = Cli.getOr "example" "all" args
let cliShots = Cli.getIntOr "shots" 500 args
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
// Define a small Max-Cut QUBO as a sparse map
// ============================================================================
// Max-Cut on a 4-node path graph: 0--1--2--3
// QUBO: minimize Q(x) = sum_{(i,j) in edges} -(x_i + x_j - 2*x_i*x_j)
// Equivalent: Q_{ii} = -degree(i), Q_{ij} = +2 for each edge

let numQubits = 4

let sparseQubo : Map<int * int, float> =
    Map.ofList [
        // Diagonal (linear) terms: -degree
        (0, 0), -1.0    // node 0: 1 edge
        (1, 1), -2.0    // node 1: 2 edges
        (2, 2), -2.0    // node 2: 2 edges
        (3, 3), -1.0    // node 3: 1 edge
        // Off-diagonal (quadratic) terms: +2 per edge
        (0, 1), 2.0
        (1, 2), 2.0
        (2, 3), 2.0
    ]

pr "Sparse QUBO for Max-Cut on path graph 0--1--2--3"
pr "  Entries: %d (vs %d in dense 4x4 matrix)" (Map.count sparseQubo) (numQubits * numQubits)
pr ""

// ============================================================================
// Example 1: Evaluate QUBO cost for specific bitstrings
// ============================================================================

if runAll || exampleName = "evaluate" then
    pr "â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”"
    pr " Example 1: Evaluate Sparse QUBO"
    pr " Compute the QUBO cost for several candidate bitstrings."
    pr "â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”"

    let candidates = [
        [| 0; 1; 0; 1 |]   // Alternating: cuts all 3 edges (optimal)
        [| 1; 0; 1; 0 |]   // Alternating: cuts all 3 edges (optimal)
        [| 1; 1; 0; 0 |]   // Cuts 1 edge
        [| 0; 0; 0; 0 |]   // All-zero: cuts 0 edges
        [| 1; 1; 1; 1 |]   // All-one: cuts 0 edges
    ]

    pr ""
    pr "  %-16s  %10s  %s" "Bitstring" "QUBO Cost" "Edges Cut"
    pr "  %-16s  %10s  %s" "----------------" "----------" "----------"

    for bits in candidates do
        let cost = QaoaExecutionHelpers.evaluateQuboSparse sparseQubo bits
        let edgesCut =
            [ (0,1); (1,2); (2,3) ]
            |> List.filter (fun (i,j) -> bits.[i] <> bits.[j])
            |> List.length
        let bitsStr = bits |> Array.map string |> String.concat ""
        pr "  %-16s  %10.1f  %d / 3" bitsStr cost edgesCut
        jsonResults <- (box {| Example = "Evaluate"; Bitstring = bitsStr; Cost = cost; EdgesCut = edgesCut |}) :: jsonResults
        csvRows <- [ "Evaluate"; bitsStr; sprintf "%.1f" cost; string edgesCut ] :: csvRows

    pr ""
    pr "  (Lower QUBO cost = more edges cut = better Max-Cut solution)"

// ============================================================================
// Example 2: Grid search over QAOA parameters (sparse)
// ============================================================================

if runAll || exampleName = "gridsearch" then
    pr ""
    pr "â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”"
    pr " Example 2: Grid Search (Sparse QUBO)"
    pr " Search QAOA parameter space to find best angles for Max-Cut."
    pr "â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”"

    let config = { QaoaExecutionHelpers.fastConfig with FinalShots = cliShots }

    let result = QaoaExecutionHelpers.executeQaoaWithGridSearchSparse quantumBackend numQubits sparseQubo config

    match result with
    | Ok (bestBits, bestParams) ->
        let bitsStr = bestBits |> Array.map string |> String.concat ""
        let cost = QaoaExecutionHelpers.evaluateQuboSparse sparseQubo bestBits
        pr "GridSearch Complete"
        pr "  Best bitstring:  %s" bitsStr
        pr "  QUBO cost:       %.2f" cost
        pr "  Parameters:      %d layers" bestParams.Length
        for i, (gamma, beta) in bestParams |> Array.indexed do
            pr "    Layer %d: gamma=%.4f, beta=%.4f" (i + 1) gamma beta
        jsonResults <- (box {| Example = "GridSearch"; Bitstring = bitsStr; Cost = cost |}) :: jsonResults
        csvRows <- [ "GridSearch"; bitsStr; sprintf "%.2f" cost; "" ] :: csvRows
    | Error e ->
        pr "GridSearch FAILED: %A" e

// ============================================================================
// Example 3: Nelder-Mead optimization (sparse)
// ============================================================================

if runAll || exampleName = "optimize" then
    pr ""
    pr "â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”"
    pr " Example 3: Nelder-Mead Optimization (Sparse QUBO)"
    pr " Optimize QAOA parameters using Nelder-Mead for better convergence."
    pr "â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”"

    let config = { QaoaExecutionHelpers.defaultConfig with FinalShots = cliShots }

    let result = QaoaExecutionHelpers.executeQaoaWithOptimizationSparse quantumBackend numQubits sparseQubo config

    match result with
    | Ok (bestBits, bestParams, converged) ->
        let bitsStr = bestBits |> Array.map string |> String.concat ""
        let cost = QaoaExecutionHelpers.evaluateQuboSparse sparseQubo bestBits
        pr "Optimization Complete"
        pr "  Best bitstring:  %s" bitsStr
        pr "  QUBO cost:       %.2f" cost
        pr "  Converged:       %b" converged
        pr "  Parameters:      %d layers" bestParams.Length
        for i, (gamma, beta) in bestParams |> Array.indexed do
            pr "    Layer %d: gamma=%.4f, beta=%.4f" (i + 1) gamma beta
        jsonResults <- (box {| Example = "Optimize"; Bitstring = bitsStr; Cost = cost; Converged = converged |}) :: jsonResults
        csvRows <- [ "Optimize"; bitsStr; sprintf "%.2f" cost; string converged ] :: csvRows
    | Error e ->
        pr "Optimization FAILED: %A" e

// ============================================================================
// Example 4: Budget-constrained execution
// ============================================================================

if runAll || exampleName = "budget" then
    pr ""
    pr "â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”"
    pr " Example 4: Budget-Constrained Execution"
    pr " Run QAOA with a total shot budget and adaptive decomposition."
    pr "â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”"

    // Budget execution requires a dense QUBO (float[,])
    let denseQubo =
        let q = Array2D.zeroCreate numQubits numQubits
        sparseQubo |> Map.iter (fun (i, j) v ->
            q.[i, j] <- v
        )
        q

    let config = QaoaExecutionHelpers.defaultConfig
    let budget : QaoaExecutionHelpers.ExecutionBudget = {
        MaxTotalShots = cliShots * 2
        MaxTimeMs = Some 10000
        Decomposition = QaoaExecutionHelpers.AdaptiveToBudgetBackend
    }

    pr "  Budget: %d total shots, %s time limit" budget.MaxTotalShots
        (match budget.MaxTimeMs with Some ms -> sprintf "%dms" ms | None -> "none")

    let result = QaoaExecutionHelpers.executeWithBudget quantumBackend denseQubo config budget

    match result with
    | Ok (bestBits, bestParams, converged) ->
        let bitsStr = bestBits |> Array.map string |> String.concat ""
        let cost = QaoaExecutionHelpers.evaluateQuboSparse sparseQubo bestBits
        pr "Budget Execution Complete"
        pr "  Best bitstring:  %s" bitsStr
        pr "  QUBO cost:       %.2f" cost
        pr "  Converged:       %b" converged
        pr "  Parameters:      %d layers" bestParams.Length
        jsonResults <- (box {| Example = "Budget"; Bitstring = bitsStr; Cost = cost; Converged = converged |}) :: jsonResults
        csvRows <- [ "Budget"; bitsStr; sprintf "%.2f" cost; string converged ] :: csvRows
    | Error e ->
        pr "Budget Execution FAILED: %A" e

// --- JSON output ---
outputPath |> Option.iter (fun path ->
    Reporting.writeJson path (jsonResults |> List.rev)
    pr "JSON written to %s" path
)

// --- CSV output ---
csvPath |> Option.iter (fun path ->
    let header = [ "Example"; "Bitstring"; "Cost"; "Extra" ]
    Reporting.writeCsv path header (csvRows |> List.rev)
    pr "CSV written to %s" path
)

// --- Usage hints ---
if not quiet && outputPath.IsNone && csvPath.IsNone then
    pr "-------------------------------------------"
    pr "Tip: Use --example evaluate|gridsearch|optimize|budget to run one."
    pr "     Use --shots N to change measurement count (default 500)."
    pr "     Use --output results.json or --csv results.csv to export."
    pr "     Use --help for all options."
