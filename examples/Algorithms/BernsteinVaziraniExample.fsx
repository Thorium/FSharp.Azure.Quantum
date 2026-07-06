// Bernstein-Vazirani Algorithm Example
// Recovers a hidden bitstring with a single quantum query
//
// Usage:
//   dotnet fsi BernsteinVaziraniExample.fsx
//   dotnet fsi BernsteinVaziraniExample.fsx -- --help
//   dotnet fsi BernsteinVaziraniExample.fsx -- --secret 1011 --shots 200 --backend local
//   dotnet fsi BernsteinVaziraniExample.fsx -- --quiet --output results.json --csv results.csv

(*
===============================================================================
 Background Theory
===============================================================================

The Bernstein-Vazirani algorithm (1997) recovers a hidden bitstring s from a
black-box function f(x) = s.x (mod 2) using a single oracle query. A classical
algorithm must query n times (once per bit: f(100...), f(010...), ...), so the
quantum algorithm gives an n-to-1 query advantage - and unlike Deutsch-Jozsa,
the answer is a full bitstring rather than one bit.

The circuit is identical in shape to Deutsch-Jozsa:
  1. Hadamard gates create superposition of all 2^n inputs
  2. Oracle applies phase: |x> -> (-1)^(s.x)|x> (phase kickback)
  3. Final Hadamards interfere all amplitudes onto the single state |s>

Key Equations:
  - Oracle action: U_s|x> = (-1)^(s.x)|x>
  - Final state: H^n U_s H^n |0>^n = |s>  (measurement yields s with certainty)
  - Classical lower bound: n queries

References:
  [1] Bernstein & Vazirani, "Quantum complexity theory",
      SIAM J. Comput. 26, 1411-1473 (1997).
  [2] Hidary, "Quantum Computing: An Applied Approach", 2nd ed.,
      Springer (2021), The Canon chapter.
  [3] Wikipedia: Bernstein-Vazirani_algorithm
      https://en.wikipedia.org/wiki/Bernstein%E2%80%93Vazirani_algorithm
*)

#r "nuget: Microsoft.Extensions.Logging.Abstractions, 10.0.0"
#r "../../src/FSharp.Azure.Quantum/bin/Debug/net10.0/FSharp.Azure.Quantum.dll"
#r "../../src/FSharp.Azure.Quantum.Topological/bin/Debug/net10.0/FSharp.Azure.Quantum.Topological.dll"
#load "../_common/Cli.fs"
#load "../_common/Data.fs"
#load "../_common/Reporting.fs"

open FSharp.Azure.Quantum.Algorithms.BernsteinVazirani
open FSharp.Azure.Quantum.Backends.LocalBackend
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Topological
open FSharp.Azure.Quantum.Examples.Common

// ============================================================================
// CLI Setup
// ============================================================================

let argv = fsi.CommandLineArgs |> Array.skip 1
let args = Cli.parse argv

Cli.exitIfHelp "BernsteinVaziraniExample.fsx" "Bernstein-Vazirani: recover a hidden bitstring with 1 quantum query." [
    { Name = "secret"; Description = "Hidden bitstring to recover (e.g. 1011)"; Default = Some "1011" }
    { Name = "shots"; Description = "Measurement shots"; Default = Some "100" }
    { Name = "backend"; Description = "Backend to use (local/topological/both)"; Default = Some "both" }
    { Name = "output"; Description = "Write results to JSON file"; Default = None }
    { Name = "csv"; Description = "Write results to CSV file"; Default = None }
    { Name = "quiet"; Description = "Suppress informational output"; Default = None }
] args

let secretArg = Cli.getOr "secret" "1011" args
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

let numQubits = secret.Length

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
    printfn "=== Bernstein-Vazirani Algorithm ==="
    printfn ""
    printfn "BUSINESS SCENARIO:"
    printfn "Recover a hidden n-bit string from a linear black-box function"
    printfn "in a single query (classical requires n queries)."
    printfn ""
    printfn "Configuration: secret = %s (%d qubits), %d shots" secretArg numQubits shots
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
            printfn "  Hidden secret:    %s" secretArg
            printfn "  Recovered secret: %s  (confidence %.2f%%)  [%s]"
                recoveredStr (result.Confidence * 100.0)
                (if correct then "OK" else "MISMATCH")
            printfn ""

        results.Add(
            [ "backend", backendKey
              "backend_name", backend.Name
              "secret", secretArg
              "recovered", recoveredStr
              "confidence", sprintf "%.4f" result.Confidence
              "qubits", string result.NumQubits
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
    printfn "--- Quantum Advantage ---"
    printfn ""
    printfn "  Classical approach (deterministic):"
    printfn "    n = %d queries (probe one bit of s per query)" numQubits
    printfn ""
    printfn "  Quantum approach (Bernstein-Vazirani):"
    printfn "    Exactly 1 query, full bitstring recovered with certainty"
    printfn ""
    printfn "  Speedup factor: %dx" numQubits
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
    printfn "  dotnet fsi BernsteinVaziraniExample.fsx -- --secret 110101 --shots 200"
    printfn "  dotnet fsi BernsteinVaziraniExample.fsx -- --backend local"
    printfn "  dotnet fsi BernsteinVaziraniExample.fsx -- --quiet --output results.json --csv results.csv"
    printfn "  dotnet fsi BernsteinVaziraniExample.fsx -- --help"
