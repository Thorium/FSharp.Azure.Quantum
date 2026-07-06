// Simon's Algorithm Example
// Finds the hidden XOR period of a two-to-one function in O(n) quantum queries
//
// Usage:
//   dotnet fsi SimonExample.fsx
//   dotnet fsi SimonExample.fsx -- --help
//   dotnet fsi SimonExample.fsx -- --secret 110 --shots 200 --backend local
//   dotnet fsi SimonExample.fsx -- --quiet --output results.json --csv results.csv

(*
===============================================================================
 Background Theory
===============================================================================

Simon's algorithm (1994) finds the hidden period s of a two-to-one function
f: {0,1}^n -> {0,1}^n satisfying f(x) = f(y) iff y = x XOR s. Classically this
needs Omega(2^(n/2)) queries (birthday bound); quantumly O(n) suffice - the
first EXPONENTIAL oracle separation, and the direct inspiration for Shor's
period-finding algorithm.

Each quantum iteration uses 2n qubits (input + output register):
  1. Hadamards on the input register create superposition of all 2^n inputs
  2. XOR oracle entangles the registers: |x>|0> -> |x>|f(x)>
  3. Hadamards on the input register, then measurement, yield a random vector
     y satisfying y.s = 0 (mod 2)
  4. After ~n independent vectors, Gaussian elimination over GF(2) recovers s

Key Equations:
  - Oracle action: U_f|x>|y> = |x>|y XOR f(x)>
  - Measurement distribution: uniform over { y : y.s = 0 (mod 2) }
  - Classical post-processing: solve the linear system { y_i.s = 0 } over GF(2)
  - Rank n    -> s = 0 (f is one-to-one)
  - Rank n-1  -> unique nonzero s

References:
  [1] Simon, "On the power of quantum computation",
      SIAM J. Comput. 26, 1474-1483 (1997).
  [2] Hidary, "Quantum Computing: An Applied Approach", 2nd ed.,
      Springer (2021), The Canon chapter.
  [3] Wikipedia: Simon's_problem
      https://en.wikipedia.org/wiki/Simon%27s_problem
*)

#r "nuget: Microsoft.Extensions.Logging.Abstractions, 10.0.0"
#r "../../src/FSharp.Azure.Quantum/bin/Debug/net10.0/FSharp.Azure.Quantum.dll"
#r "../../src/FSharp.Azure.Quantum.Topological/bin/Debug/net10.0/FSharp.Azure.Quantum.Topological.dll"
#load "../_common/Cli.fs"
#load "../_common/Data.fs"
#load "../_common/Reporting.fs"

open FSharp.Azure.Quantum.Algorithms.Simon
open FSharp.Azure.Quantum.Backends.LocalBackend
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Topological
open FSharp.Azure.Quantum.Examples.Common

// ============================================================================
// CLI Setup
// ============================================================================

let argv = fsi.CommandLineArgs |> Array.skip 1
let args = Cli.parse argv

Cli.exitIfHelp "SimonExample.fsx" "Simon: find the hidden XOR period of a two-to-one function." [
    { Name = "secret"; Description = "Hidden XOR period (e.g. 110; all zeros = one-to-one)"; Default = Some "110" }
    { Name = "shots"; Description = "Measurement shots"; Default = Some "100" }
    { Name = "backend"; Description = "Backend to use (local/topological/both)"; Default = Some "both" }
    { Name = "output"; Description = "Write results to JSON file"; Default = None }
    { Name = "csv"; Description = "Write results to CSV file"; Default = None }
    { Name = "quiet"; Description = "Suppress informational output"; Default = None }
] args

let secretArg = Cli.getOr "secret" "110" args
let shots = Cli.getIntOr "shots" 100 args
let backendArg = Cli.getOr "backend" "both" args
let quiet = Cli.hasFlag "quiet" args
let outputPath = Cli.tryGet "output" args
let csvPath = Cli.tryGet "csv" args

let secret =
    secretArg
    |> Seq.map (fun c ->
        match c with
        | '0' -> 0
        | '1' -> 1
        | _ -> failwithf "Invalid secret '%s': must contain only 0 and 1" secretArg)
    |> Seq.toArray

let numInputQubits = secret.Length

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
// Run Across Backends
// ============================================================================

let results = System.Collections.Generic.List<Map<string, string>>()

if not quiet then
    printfn "=== Simon's Algorithm ==="
    printfn ""
    printfn "BUSINESS SCENARIO:"
    printfn "Find the hidden XOR period of a two-to-one black-box function"
    printfn "in O(n) queries (classical requires ~2^(n/2) queries)."
    printfn ""
    printfn "Configuration: secret = %s (%d input qubits, %d circuit qubits), %d shots"
        secretArg numInputQubits (2 * numInputQubits) shots
    printfn ""

for (backendKey, backend) in backendsToTest do
    if not quiet then
        printfn "--- Backend: %s (%s) ---" backend.Name backendKey
        printfn "  Native state type: %A" backend.NativeStateType
        printfn ""

    match runWithSecret secret backend shots with
    | Ok result ->
        let recoveredStr = result.RecoveredSecret |> Array.map string |> String.concat ""
        let correct = recoveredStr = secretArg

        if not quiet then
            printfn "  Hidden period:    %s" secretArg
            printfn "  Recovered period: %s  (%s)  [%s]"
                recoveredStr
                (if result.IsOneToOne then "one-to-one, s = 0" else "two-to-one")
                (if correct then "OK" else "MISMATCH")
            printfn "  Distinct GF(2) equations collected: %d" result.Equations.Length
            printfn ""

        results.Add(
            [ "backend", backendKey
              "backend_name", backend.Name
              "secret", secretArg
              "recovered", recoveredStr
              "one_to_one", string result.IsOneToOne
              "equations", string result.Equations.Length
              "input_qubits", string result.NumInputQubits
              "shots", string result.Shots
              "correct", string correct ]
            |> Map.ofList)

    | Error err ->
        if not quiet then
            printfn "  ERROR: %A" err
            printfn ""

        results.Add(
            [ "backend", backendKey
              "secret", secretArg
              "error", sprintf "%A" err ]
            |> Map.ofList)

// ============================================================================
// Quantum Advantage Summary
// ============================================================================

if not quiet then
    let classicalQueries = 1 <<< (numInputQubits / 2)
    printfn "--- Quantum Advantage ---"
    printfn ""
    printfn "  Classical approach (birthday bound):"
    printfn "    ~2^(n/2) = %d queries for n=%d" classicalQueries numInputQubits
    printfn ""
    printfn "  Quantum approach (Simon):"
    printfn "    O(n) oracle queries + GF(2) Gaussian elimination"
    printfn ""
    printfn "  This exponential separation inspired Shor's factoring algorithm."
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
    printfn "  dotnet fsi SimonExample.fsx -- --secret 1010 --shots 200"
    printfn "  dotnet fsi SimonExample.fsx -- --backend local --secret 000"
    printfn "  dotnet fsi SimonExample.fsx -- --quiet --output results.json --csv results.csv"
    printfn "  dotnet fsi SimonExample.fsx -- --help"
