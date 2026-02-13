// Deutsch-Jozsa Algorithm Example
// Determines if a function is constant or balanced with a single quantum query
//
// Usage:
//   dotnet fsi DeutschJozsaExample.fsx
//   dotnet fsi DeutschJozsaExample.fsx -- --help
//   dotnet fsi DeutschJozsaExample.fsx -- --qubits 4 --shots 200 --backend local
//   dotnet fsi DeutschJozsaExample.fsx -- --quiet --output results.json --csv results.csv

(*
===============================================================================
 Background Theory
===============================================================================

The Deutsch-Jozsa algorithm (1992) was the first quantum algorithm to demonstrate
exponential speedup over classical deterministic computation. Given a black-box
function f:{0,1}^n -> {0,1} promised to be either "constant" (same output for all
inputs) or "balanced" (outputs 0 for exactly half the inputs and 1 for the other
half), the algorithm determines which case applies with certainty using only ONE
query to f, whereas any classical deterministic algorithm requires 2^(n-1)+1
queries in the worst case.

The algorithm exploits quantum parallelism and interference:
  1. Hadamard gates create superposition of all 2^n inputs
  2. Oracle applies phase: |x> -> (-1)^f(x)|x> (phase kickback)
  3. Final Hadamards cause interference:
     - Constant f: constructive at |0>^n (measure all zeros)
     - Balanced f: destructive at |0>^n (measure non-zero)

Key Equations:
  - Oracle action (phase kickback): U_f|x>|-> = (-1)^f(x)|x>|->
  - Initial superposition: H^n|0>^n = (1/sqrt(2^n)) sum_x |x>
  - Final measurement: |<0^n|H^n U_f H^n|0^n>|^2 = 1 if constant, 0 if balanced
  - Classical lower bound: 2^(n-1) + 1 queries (deterministic)

Unified Backend Architecture:
  This example demonstrates how the same Deutsch-Jozsa algorithm works across
  different quantum backends through the IQuantumBackend interface:
  - LocalBackend: Traditional gate-based simulation using state vectors
  - TopologicalBackend: Braiding-based computation using Ising anyons

References:
  [1] Deutsch & Jozsa, "Rapid solution of problems by quantum computation",
      Proc. R. Soc. Lond. A 439, 553-558 (1992).
  [2] Cleve et al., "Quantum algorithms revisited",
      Proc. R. Soc. Lond. A 454, 339-354 (1998).
  [3] Nielsen & Chuang, "Quantum Computation and Quantum Information",
      Cambridge University Press (2010), Section 1.4.3.
  [4] Wikipedia: Deutsch-Jozsa_algorithm
      https://en.wikipedia.org/wiki/Deutsch%E2%80%93Jozsa_algorithm
*)

#r "../../src/FSharp.Azure.Quantum/bin/Debug/net10.0/FSharp.Azure.Quantum.dll"
#r "../../src/FSharp.Azure.Quantum.Topological/bin/Debug/net10.0/FSharp.Azure.Quantum.Topological.dll"
#load "../_common/Cli.fs"
#load "../_common/Data.fs"
#load "../_common/Reporting.fs"

open FSharp.Azure.Quantum.Algorithms.DeutschJozsa
open FSharp.Azure.Quantum.Backends.LocalBackend
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Topological
open FSharp.Azure.Quantum.Examples.Common

// ============================================================================
// CLI Setup
// ============================================================================

let argv = fsi.CommandLineArgs |> Array.skip 1
let args = Cli.parse argv

Cli.exitIfHelp "DeutschJozsaExample.fsx" "Deutsch-Jozsa: determine constant vs balanced with 1 quantum query." [
    { Name = "qubits"; Description = "Number of qubits"; Default = Some "3" }
    { Name = "shots"; Description = "Measurement shots per oracle"; Default = Some "100" }
    { Name = "backend"; Description = "Backend to use (local/topological/both)"; Default = Some "both" }
    { Name = "oracle"; Description = "Oracle to test (zero/one/firstbit/parity/all)"; Default = Some "all" }
    { Name = "output"; Description = "Write results to JSON file"; Default = None }
    { Name = "csv"; Description = "Write results to CSV file"; Default = None }
    { Name = "quiet"; Description = "Suppress informational output"; Default = None }
] args

let numQubits = Cli.getIntOr "qubits" 3 args
let shots = Cli.getIntOr "shots" 100 args
let backendArg = Cli.getOr "backend" "both" args
let oracleArg = Cli.getOr "oracle" "all" args
let quiet = Cli.hasFlag "quiet" args
let outputPath = Cli.tryGet "output" args
let csvPath = Cli.tryGet "csv" args

// ============================================================================
// Quantum Backends
// ============================================================================

let localBackend = LocalBackend() :> IQuantumBackend
let topoBackend = TopologicalUnifiedBackendFactory.createIsing 16

let backendsToTest =
    match backendArg with
    | "local" -> [ ("local", localBackend) ]
    | "topological" | "topo" -> [ ("topological", topoBackend) ]
    | _ -> [ ("local", localBackend); ("topological", topoBackend) ]

// ============================================================================
// Result Collection
// ============================================================================

let results = System.Collections.Generic.List<Map<string, string>>()

// ============================================================================
// Oracle Definitions
// ============================================================================

type OracleSpec = {
    Name: string
    Key: string
    Expected: string
    Description: string
    Run: int -> IQuantumBackend -> int -> Result<DeutschJozsaResult, FSharp.Azure.Quantum.Core.QuantumError>
}

let allOracles = [
    { Name = "Constant-Zero"; Key = "zero"; Expected = "Constant"
      Description = "f(x) = 0 for all x"
      Run = fun n b s -> runConstantZero n b s }
    { Name = "Constant-One"; Key = "one"; Expected = "Constant"
      Description = "f(x) = 1 for all x"
      Run = fun n b s -> runConstantOne n b s }
    { Name = "Balanced First-Bit"; Key = "firstbit"; Expected = "Balanced"
      Description = "f(x) = first bit of x"
      Run = fun n b s -> runBalancedFirstBit n b s }
    { Name = "Balanced Parity"; Key = "parity"; Expected = "Balanced"
      Description = "f(x) = XOR of all bits"
      Run = fun n b s -> runBalancedParity n b s }
]

let oraclesToTest =
    match oracleArg with
    | "all" -> allOracles
    | key -> allOracles |> List.filter (fun o -> o.Key = key)

// ============================================================================
// Run Tests Across Backends
// ============================================================================

if not quiet then
    printfn "=== Deutsch-Jozsa Algorithm ==="
    printfn ""
    printfn "BUSINESS SCENARIO:"
    printfn "Determine if a black-box function is constant or balanced"
    printfn "using a single quantum query (exponential speedup over classical)."
    printfn ""
    printfn "Configuration: %d qubits (search space = %d), %d shots" numQubits (1 <<< numQubits) shots
    printfn ""

for (backendKey, backend) in backendsToTest do
    if not quiet then
        printfn "--- Backend: %s (%s) ---" backend.Name backendKey
        printfn "  Native state type: %A" backend.NativeStateType
        printfn ""

    for oracle in oraclesToTest do
        match oracle.Run numQubits backend shots with
        | Ok result ->
            let correct = (sprintf "%A" result.OracleType) = oracle.Expected

            if not quiet then
                printfn "  %-22s [%s]" oracle.Name oracle.Description
                printfn "    Result: %A  (expected: %s)  P(|0>^n) = %.4f  [%s]"
                    result.OracleType oracle.Expected result.ZeroProbability
                    (if correct then "OK" else "MISMATCH")
                printfn ""

            results.Add(
                [ "backend", backendKey
                  "backend_name", backend.Name
                  "oracle", oracle.Key
                  "oracle_name", oracle.Name
                  "description", oracle.Description
                  "expected", oracle.Expected
                  "result", sprintf "%A" result.OracleType
                  "zero_probability", sprintf "%.4f" result.ZeroProbability
                  "qubits", string result.NumQubits
                  "shots", string result.Shots
                  "correct", string correct ]
                |> Map.ofList)

        | Error err ->
            if not quiet then
                printfn "  %-22s ERROR: %A" oracle.Name err
                printfn ""

            results.Add(
                [ "backend", backendKey
                  "oracle", oracle.Key
                  "oracle_name", oracle.Name
                  "error", sprintf "%A" err ]
                |> Map.ofList)

// ============================================================================
// Quantum Advantage Summary
// ============================================================================

if not quiet then
    let classicalQueries = (1 <<< (numQubits - 1)) + 1
    printfn "--- Quantum Advantage ---"
    printfn ""
    printfn "  Classical approach (deterministic):"
    printfn "    Worst case: 2^(n-1) + 1 = %d queries for n=%d" classicalQueries numQubits
    printfn "    Must check >50%% of inputs to be certain"
    printfn ""
    printfn "  Quantum approach (Deutsch-Jozsa):"
    printfn "    Exactly 1 query to oracle"
    printfn "    100%% certainty (theory); ~95%% on NISQ hardware with noise"
    printfn ""
    printfn "  Speedup factor: %dx" classicalQueries
    printfn ""

    if backendArg = "both" || backendsToTest.Length > 1 then
        printfn "--- Unified Architecture ---"
        printfn ""
        printfn "  Same algorithm code works on BOTH backends:"
        printfn "    LocalBackend:        Gate-based simulation (state vectors)"
        printfn "    TopologicalBackend:  Braiding-based (Ising anyons)"
        printfn ""
        printfn "  Note: Topological backend may show approximate results for"
        printfn "  balanced oracles due to Solovay-Kitaev gate decomposition."
        printfn ""

// ============================================================================
// Structured Output
// ============================================================================

let resultsList = results |> Seq.toList

match outputPath with
| Some path -> Reporting.writeJson path resultsList
| None -> ()

match csvPath with
| Some path ->
    let allKeys =
        resultsList
        |> List.collect (fun m -> m |> Map.toList |> List.map fst)
        |> List.distinct
    let rows =
        resultsList
        |> List.map (fun m -> allKeys |> List.map (fun k -> m |> Map.tryFind k |> Option.defaultValue ""))
    Reporting.writeCsv path allKeys rows
| None -> ()

// ============================================================================
// Usage Hints
// ============================================================================

if not quiet && outputPath.IsNone && csvPath.IsNone && argv.Length = 0 then
    printfn "Hint: Customize this run with CLI options:"
    printfn "  dotnet fsi DeutschJozsaExample.fsx -- --qubits 4 --shots 200"
    printfn "  dotnet fsi DeutschJozsaExample.fsx -- --backend local --oracle parity"
    printfn "  dotnet fsi DeutschJozsaExample.fsx -- --quiet --output results.json --csv results.csv"
    printfn "  dotnet fsi DeutschJozsaExample.fsx -- --help"
