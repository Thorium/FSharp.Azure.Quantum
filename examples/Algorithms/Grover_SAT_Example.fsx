// Grover SAT Solver Example
// Demonstrates using Grover's algorithm to solve Boolean Satisfiability (SAT) problems
//
// Usage:
//   dotnet fsi Grover_SAT_Example.fsx
//   dotnet fsi Grover_SAT_Example.fsx -- --help
//   dotnet fsi Grover_SAT_Example.fsx -- --example 2
//   dotnet fsi Grover_SAT_Example.fsx -- --shots 2000 --example all
//   dotnet fsi Grover_SAT_Example.fsx -- --quiet --output results.json --csv results.csv

(*
===============================================================================
 Background Theory
===============================================================================

Grover's algorithm (1996) provides a quadratic speedup for unstructured search
problems. Given a search space of N items with M "marked" solutions, classical
algorithms require O(N/M) queries on average, while Grover's algorithm finds a
solution with high probability in O(sqrt(N/M)) queries. For SAT problems with n
variables, the search space is N = 2^n, so Grover reduces the search from O(2^n)
to O(2^(n/2)) - a substantial speedup for large instances.

The algorithm works by repeatedly applying two operations: (1) an oracle that
marks solutions by flipping their phase, and (2) a diffusion operator that
amplifies the amplitude of marked states. After approximately pi/4 * sqrt(N/M)
iterations, measuring the quantum state yields a solution with probability
approaching 1. For SAT, the oracle encodes the Boolean formula such that
satisfying assignments receive a phase flip.

Key Equations:
  - Grover iterate: G = (2|psi><psi| - I) * O_f  where O_f is the oracle
  - Initial state: |psi> = H^n|0>^n = (1/sqrt(N)) sum_x|x>  (uniform superposition)
  - Optimal iterations: k ~ (pi/4)sqrt(N/M) for M solutions in N items
  - Success probability: P(success) = sin^2((2k+1)theta) where sin^2(theta) = M/N
  - SAT oracle: O_f|x> = (-1)^f(x)|x> where f(x) = 1 iff x satisfies formula

Quantum Advantage:
  Grover's speedup is provably optimal for unstructured search (BBBV theorem).
  For SAT, this means finding satisfying assignments in O(2^(n/2)) vs O(2^n).
  While this doesn't solve NP-complete problems in polynomial time, it provides
  meaningful speedup for cryptanalysis (AES key search: 2^128 -> 2^64) and
  verification tasks. Hybrid classical-quantum approaches combine Grover with
  classical preprocessing for practical SAT solving.

References:
  [1] Grover, "A fast quantum mechanical algorithm for database search",
      STOC '96, pp. 212-219. https://doi.org/10.1145/237814.237866
  [2] Boyer et al., "Tight bounds on quantum searching",
      Fortschr. Phys. 46, 493-505 (1998). https://arxiv.org/abs/quant-ph/9605034
  [3] Ambainis, "Quantum search algorithms", SIGACT News 35(2), 22-35 (2004).
      https://doi.org/10.1145/992287.992296
  [4] Wikipedia: Grover's_algorithm
      https://en.wikipedia.org/wiki/Grover%27s_algorithm
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

Cli.exitIfHelp "Grover_SAT_Example.fsx" "Solve Boolean Satisfiability (SAT) problems using Grover's quantum search." [
    { Name = "example"; Description = "Which example to run (1/2/3/all)"; Default = Some "all" }
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

/// Extract variable assignment bits from an integer solution
let extractVars (numVars: int) (solution: int) : bool array =
    [| for i in 0 .. numVars - 1 -> (solution >>> i) &&& 1 = 1 |]

/// Format variable assignment as a string
let formatAssignment (vars: bool array) =
    vars |> Array.mapi (fun i v -> sprintf "x%d=%b" i v) |> String.concat ", "

type ExampleResult = {
    Example: string
    Formula: string
    NumVariables: int
    SearchSpace: int
    ClassicalSolutions: int list
    QuantumSolutions: int list
    SuccessProbability: float
    Iterations: int
    Shots: int
    Status: string
}

let allResults = System.Collections.Generic.List<ExampleResult>()

// ============================================================================
// Example 1: Simple 2-SAT Problem
// ============================================================================
//
// Formula: (x0 OR x1) AND (NOT x0 OR x1)
// Expected solutions: Any assignment where x1 = true
//   - 0b01 (x0=false, x1=true) = 1
//   - 0b11 (x0=true, x1=true) = 3

if shouldRun "1" then
    if not quiet then
        printfn "=== Example 1: Simple 2-SAT ==="
        printfn "Formula: (x0 OR x1) AND (NOT x0 OR x1)"
        printfn ""

    let simple2SAT = {
        NumVariables = 2
        Clauses = [
            clause [var 0; var 1]       // x0 OR x1
            clause [notVar 0; var 1]    // NOT x0 OR x1
        ]
    }

    match satOracle simple2SAT with
    | Error err ->
        if not quiet then printfn "Failed to create oracle: %A" err
        allResults.Add({
            Example = "1-Simple-2SAT"; Formula = "(x0 OR x1) AND (NOT x0 OR x1)"
            NumVariables = 2; SearchSpace = 4; ClassicalSolutions = []; QuantumSolutions = []
            SuccessProbability = 0.0; Iterations = 0; Shots = shots; Status = sprintf "Oracle error: %A" err
        })
    | Ok oracle ->
        if not quiet then
            printfn "Oracle created: %d qubits, search space = %d states" oracle.NumQubits (1 <<< oracle.NumQubits)

        // Find classical solutions for verification
        let classicalSols =
            [ for i in 0 .. (1 <<< oracle.NumQubits) - 1 do
                if Oracle.isSolution oracle.Spec i then yield i ]

        if not quiet then
            printfn ""
            printfn "Classical verification (all assignments):"
            for i in 0 .. (1 <<< oracle.NumQubits) - 1 do
                let vars = extractVars 2 i
                let isSol = Oracle.isSolution oracle.Spec i
                let mark = if isSol then "SOLUTION" else "       "
                printfn "  %s  %s (assignment=%d)" mark (formatAssignment vars) i
            printfn ""
            printfn "Running Grover's algorithm (%d shots)..." shots

        // Create quantum backend (Rule 1: IQuantumBackend)
        let backend = LocalBackend() :> IQuantumBackend
        let config = { Grover.defaultConfig with Shots = shots }

        match Grover.search oracle backend config with
        | Error err ->
            if not quiet then printfn "Search failed: %A" err
            allResults.Add({
                Example = "1-Simple-2SAT"; Formula = "(x0 OR x1) AND (NOT x0 OR x1)"
                NumVariables = 2; SearchSpace = 4; ClassicalSolutions = classicalSols; QuantumSolutions = []
                SuccessProbability = 0.0; Iterations = 0; Shots = shots; Status = sprintf "Search error: %A" err
            })
        | Ok result ->
            if not quiet then
                if result.Solutions.IsEmpty then
                    printfn "  No solution found"
                else
                    for solution in result.Solutions do
                        let vars = extractVars 2 solution
                        printfn "  Found solution: %s (assignment=%d)" (formatAssignment vars) solution
                    printfn "  Success probability: %.2f%%" (result.SuccessProbability * 100.0)
                    printfn "  Iterations used: %d" result.Iterations

            allResults.Add({
                Example = "1-Simple-2SAT"; Formula = "(x0 OR x1) AND (NOT x0 OR x1)"
                NumVariables = 2; SearchSpace = 4; ClassicalSolutions = classicalSols
                QuantumSolutions = result.Solutions
                SuccessProbability = result.SuccessProbability
                Iterations = result.Iterations; Shots = shots
                Status = if result.Solutions.IsEmpty then "No solution" else "OK"
            })

    if not quiet then
        printfn ""
        printfn "================================================"
        printfn ""

// ============================================================================
// Example 2: 3-SAT Problem (NP-Complete)
// ============================================================================
//
// Formula: (x0 OR x1 OR x2) AND (NOT x0 OR NOT x1 OR x2) AND (x0 OR NOT x2)
// This is a 3-SAT instance (all clauses have exactly 3 literals)

if shouldRun "2" then
    if not quiet then
        printfn "=== Example 2: 3-SAT Problem ==="
        printfn "Formula: (x0 OR x1 OR x2) AND (NOT x0 OR NOT x1 OR x2) AND (x0 OR NOT x2)"
        printfn ""

    let threeSAT = {
        NumVariables = 3
        Clauses = [
            clause [var 0; var 1; var 2]           // x0 OR x1 OR x2
            clause [notVar 0; notVar 1; var 2]    // NOT x0 OR NOT x1 OR x2
            clause [var 0; notVar 2]               // x0 OR NOT x2
        ]
    }

    match satOracle threeSAT with
    | Error err ->
        if not quiet then printfn "Failed to create oracle: %A" err
        allResults.Add({
            Example = "2-3SAT-NP-Complete"; Formula = "(x0|x1|x2) AND (!x0|!x1|x2) AND (x0|!x2)"
            NumVariables = 3; SearchSpace = 8; ClassicalSolutions = []; QuantumSolutions = []
            SuccessProbability = 0.0; Iterations = 0; Shots = shots; Status = sprintf "Oracle error: %A" err
        })
    | Ok oracle ->
        if not quiet then
            printfn "Oracle created: %d qubits, search space = %d states" oracle.NumQubits (1 <<< oracle.NumQubits)

        let classicalSols =
            [ for i in 0 .. (1 <<< oracle.NumQubits) - 1 do
                if Oracle.isSolution oracle.Spec i then yield i ]

        if not quiet then
            printfn ""
            printfn "All satisfying assignments (classical verification):"
            for sol in classicalSols do
                let vars = extractVars 3 sol
                printfn "  %s (assignment=%d)" (formatAssignment vars) sol
            printfn "Total solutions: %d out of %d" classicalSols.Length (1 <<< oracle.NumQubits)
            printfn ""
            printfn "Running Grover's algorithm (%d shots)..." shots

        let backend = LocalBackend() :> IQuantumBackend
        let config = { Grover.defaultConfig with Shots = shots }

        match Grover.search oracle backend config with
        | Error err ->
            if not quiet then printfn "Search failed: %A" err
            allResults.Add({
                Example = "2-3SAT-NP-Complete"; Formula = "(x0|x1|x2) AND (!x0|!x1|x2) AND (x0|!x2)"
                NumVariables = 3; SearchSpace = 8; ClassicalSolutions = classicalSols; QuantumSolutions = []
                SuccessProbability = 0.0; Iterations = 0; Shots = shots; Status = sprintf "Search error: %A" err
            })
        | Ok result ->
            if not quiet then
                if result.Solutions.IsEmpty then
                    printfn "  No solution found"
                else
                    for solution in result.Solutions do
                        let vars = extractVars 3 solution
                        printfn "  Found solution: %s (assignment=%d)" (formatAssignment vars) solution
                    printfn "  Success probability: %.2f%%" (result.SuccessProbability * 100.0)
                    printfn "  Iterations used: %d" result.Iterations

            allResults.Add({
                Example = "2-3SAT-NP-Complete"; Formula = "(x0|x1|x2) AND (!x0|!x1|x2) AND (x0|!x2)"
                NumVariables = 3; SearchSpace = 8; ClassicalSolutions = classicalSols
                QuantumSolutions = result.Solutions
                SuccessProbability = result.SuccessProbability
                Iterations = result.Iterations; Shots = shots
                Status = if result.Solutions.IsEmpty then "No solution" else "OK"
            })

    if not quiet then
        printfn ""
        printfn "================================================"
        printfn ""

// ============================================================================
// Example 3: UNSAT Formula (No Solutions)
// ============================================================================
//
// Formula: (x0) AND (NOT x0)
// This is unsatisfiable - no assignment can satisfy both clauses

if shouldRun "3" then
    if not quiet then
        printfn "=== Example 3: UNSAT Formula ==="
        printfn "Formula: (x0) AND (NOT x0)"
        printfn ""

    let unsatFormula = {
        NumVariables = 1
        Clauses = [
            clause [var 0]      // x0
            clause [notVar 0]   // NOT x0
        ]
    }

    match satOracle unsatFormula with
    | Error err ->
        if not quiet then printfn "Failed to create oracle: %A" err
        allResults.Add({
            Example = "3-UNSAT"; Formula = "(x0) AND (NOT x0)"
            NumVariables = 1; SearchSpace = 2; ClassicalSolutions = []; QuantumSolutions = []
            SuccessProbability = 0.0; Iterations = 0; Shots = shots; Status = sprintf "Oracle error: %A" err
        })
    | Ok oracle ->
        if not quiet then
            printfn "Oracle created: %d qubits" oracle.NumQubits

        let classicalSols =
            [ for i in 0 .. (1 <<< oracle.NumQubits) - 1 do
                if Oracle.isSolution oracle.Spec i then yield i ]

        if not quiet then
            printfn ""
            printfn "Checking all possible assignments:"
            for i in 0 .. (1 <<< oracle.NumQubits) - 1 do
                let x0 = (i &&& 1) = 1
                printfn "  x0=%b (assignment=%d) - NOT a solution" x0 i
            printfn ""
            printfn "Total solutions: %d (formula is UNSATISFIABLE)" classicalSols.Length
            printfn ""
            printfn "Running Grover's algorithm (expected to find no solution)..."

        let backend = LocalBackend() :> IQuantumBackend
        let config = { Grover.defaultConfig with Shots = shots }

        match Grover.search oracle backend config with
        | Error err ->
            if not quiet then printfn "Search failed: %A" err
            allResults.Add({
                Example = "3-UNSAT"; Formula = "(x0) AND (NOT x0)"
                NumVariables = 1; SearchSpace = 2; ClassicalSolutions = []; QuantumSolutions = []
                SuccessProbability = 0.0; Iterations = 0; Shots = shots; Status = sprintf "Search error: %A" err
            })
        | Ok result ->
            if not quiet then
                if result.Solutions.IsEmpty then
                    printfn "  Correctly found no solution (formula is UNSAT)"
                else
                    printfn "  Unexpected: Found assignments %A (may be false positives)" result.Solutions

            allResults.Add({
                Example = "3-UNSAT"; Formula = "(x0) AND (NOT x0)"
                NumVariables = 1; SearchSpace = 2; ClassicalSolutions = classicalSols
                QuantumSolutions = result.Solutions
                SuccessProbability = result.SuccessProbability
                Iterations = result.Iterations; Shots = shots
                Status = if result.Solutions.IsEmpty then "UNSAT confirmed" else "False positive"
            })

    if not quiet then
        printfn ""
        printfn "================================================"
        printfn ""

// ============================================================================
// Summary
// ============================================================================

if not quiet then
    printfn "SAT Solver Summary"
    printfn "=================="
    for r in allResults do
        printfn ""
        printfn "  %s" r.Example
        printfn "    Formula: %s" r.Formula
        printfn "    Variables: %d, Search space: %d" r.NumVariables r.SearchSpace
        printfn "    Classical solutions: %A" r.ClassicalSolutions
        printfn "    Quantum solutions:   %A" r.QuantumSolutions
        printfn "    Probability: %.2f%%, Iterations: %d" (r.SuccessProbability * 100.0) r.Iterations
        printfn "    Status: %s" r.Status
    printfn ""
    printfn "Key Takeaways:"
    printfn "  1. SAT oracles enable quantum search for satisfying assignments"
    printfn "  2. Grover provides quadratic speedup: O(sqrt(N)) vs O(N) classical"
    printfn "  3. Works for both satisfiable and unsatisfiable formulas"
    printfn "  4. Scales to larger problems (limited by qubit count)"
    printfn "  5. 3-SAT is NP-complete - quantum advantage is significant!"

// ============================================================================
// Output
// ============================================================================

let resultRecords =
    allResults
    |> Seq.toList
    |> List.map (fun r ->
        {| Example = r.Example
           Formula = r.Formula
           NumVariables = r.NumVariables
           SearchSpace = r.SearchSpace
           ClassicalSolutions = r.ClassicalSolutions
           QuantumSolutions = r.QuantumSolutions
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
    let header = ["Example"; "Formula"; "NumVariables"; "SearchSpace"; "ClassicalSolutions"; "QuantumSolutions"; "SuccessProbability"; "Iterations"; "Shots"; "Status"]
    let rows =
        allResults
        |> Seq.toList
        |> List.map (fun r ->
            [ r.Example; r.Formula; string r.NumVariables; string r.SearchSpace
              sprintf "%A" r.ClassicalSolutions; sprintf "%A" r.QuantumSolutions
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
