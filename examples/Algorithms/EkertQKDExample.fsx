// Ekert E91 Quantum Key Distribution Protocol Example
// Demonstrates entanglement-based QKD with CHSH eavesdropper detection
//
// Usage:
//   dotnet fsi EkertQKDExample.fsx
//   dotnet fsi EkertQKDExample.fsx -- --help
//   dotnet fsi EkertQKDExample.fsx -- --pairs 500 --seed 42
//   dotnet fsi EkertQKDExample.fsx -- --quiet --output results.json --csv results.csv

(*
===============================================================================
 Background Theory
===============================================================================

The E91 protocol was proposed by Artur Ekert in 1991. It uses entangled Bell
pairs and the CHSH inequality to establish a shared secret key.

Protocol Steps:
  1. Source generates Bell pairs |Phi+> = (|00> + |11>) / sqrt(2)
  2. Alice measures her qubit in one of 3 bases: {0 deg, 45 deg, 90 deg}
  3. Bob measures his qubit in one of 3 bases: {0 deg, 45 deg, 135 deg}
  4. Both publicly announce basis choices (not results)
  5. Matching bases (0,0) and (45,45): used for key bits (~22% of pairs)
  6. Non-matching bases: compute CHSH parameter S for security test
  7. If |S| ~ 2*sqrt(2): secure. If |S| <= 2: eavesdropper detected

CHSH Inequality:
  S = E(a1,b1) - E(a1,b3) + E(a3,b1) + E(a3,b3)

  where E(a,b) = P(same) - P(different) for basis pair (a,b)
  
  Classical bound: |S| <= 2
  Quantum bound:   |S| = 2*sqrt(2) ~ 2.828

Key Insight: Unlike BB84 (prepare-and-measure), E91 derives security from
Bell's theorem. Eavesdropping destroys entanglement, reducing |S| below
the quantum bound 2*sqrt(2).

Production Use Cases:
  - Quantum Networks (entanglement-based secure key exchange)
  - Device-Independent QKD (security relies only on Bell violation)
  - Satellite QKD (demonstrated on Micius satellite over 1200km)

References:
  [1] Ekert, "Quantum Cryptography Based on Bell's Theorem",
      Phys. Rev. Lett. 67, 661 (1991).
  [2] Clauser, Horne, Shimony, Holt, "Proposed Experiment to Test Local
      Hidden-Variable Theories", Phys. Rev. Lett. 23, 880 (1969).
  [3] Nielsen & Chuang, "Quantum Computation and Quantum Information",
      Cambridge University Press (2010), Chapter 12.
  [4] Wikipedia: Ekert protocol
      https://en.wikipedia.org/wiki/Quantum_key_distribution#E91_protocol
*)

#r "../../src/FSharp.Azure.Quantum/bin/Debug/net10.0/FSharp.Azure.Quantum.dll"
#load "../_common/Cli.fs"
#load "../_common/Data.fs"
#load "../_common/Reporting.fs"

open FSharp.Azure.Quantum.Algorithms.EkertQKD
open FSharp.Azure.Quantum.Backends.LocalBackend
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Examples.Common

// ============================================================================
// CLI Setup
// ============================================================================

let argv = fsi.CommandLineArgs |> Array.skip 1
let args = Cli.parse argv

Cli.exitIfHelp "EkertQKDExample.fsx" "E91 entanglement-based QKD with CHSH eavesdropper detection." [
    { Name = "pairs"; Description = "Number of entangled pairs per run"; Default = Some "200" }
    { Name = "seed"; Description = "Random seed (omit for random)"; Default = Some "42" }
    { Name = "scenario"; Description = "Scenario to run (basis/secure/chsh/eve/report/all)"; Default = Some "all" }
    { Name = "output"; Description = "Write results to JSON file"; Default = None }
    { Name = "csv"; Description = "Write results to CSV file"; Default = None }
    { Name = "quiet"; Description = "Suppress informational output"; Default = None }
] args

let numPairs = Cli.getIntOr "pairs" 200 args
let seed = Cli.tryGet "seed" args |> Option.map int |> Option.orElse (Some 42)
let scenarioArg = Cli.getOr "scenario" "all" args
let quiet = Cli.hasFlag "quiet" args
let outputPath = Cli.tryGet "output" args
let csvPath = Cli.tryGet "csv" args

// ============================================================================
// Quantum Backend
// ============================================================================

let backend = LocalBackend() :> IQuantumBackend

// ============================================================================
// Result Collection
// ============================================================================

let results = System.Collections.Generic.List<Map<string, string>>()

let runScenario name = scenarioArg = "all" || scenarioArg = name

// ============================================================================
// Scenario 1: Basis Combination Table
// ============================================================================

if runScenario "basis" then
    if not quiet then
        printfn "=== Ekert E91 QKD Protocol ==="
        printfn ""
        printfn "BUSINESS SCENARIO:"
        printfn "Entanglement-based quantum key distribution with security"
        printfn "guaranteed by Bell's theorem — any eavesdropping destroys"
        printfn "entanglement and is detected via CHSH inequality violation."
        printfn ""
        printfn "Backend: %s" backend.Name
        printfn ""
        printfn "--- Scenario 1: E91 Basis Combinations ---"
        printfn ""
        printfn "%s" (formatBasisTable ())

    results.Add(
        [ "scenario", "basis_table"
          "description", "E91 basis combinations (Alice: 0/45/90 deg, Bob: 0/45/135 deg)"
          "matching_bases", "2 of 9 combinations used for key sifting"
          "test_bases", "4 combinations used for CHSH security test" ]
        |> Map.ofList)

// ============================================================================
// Scenario 2: Secure Key Exchange (No Eavesdropper)
// ============================================================================

if runScenario "secure" then
    if not quiet then
        printfn "--- Scenario 2: Secure Key Exchange (No Eavesdropper) ---"
        printfn ""

    match run backend numPairs seed with
    | Error err ->
        if not quiet then printfn "  ERROR: %A" err
        results.Add([ "scenario", "secure_exchange"; "error", sprintf "%A" err ] |> Map.ofList)

    | Ok result ->
        if not quiet then
            printfn "  Total pairs: %d" result.TotalPairs
            printfn "  Sifted key length: %d bits" result.SiftedKeyLength
            printfn "  Key rate: %.1f%%" (result.KeyRate * 100.0)
            printfn "  CHSH parameter |S|: %.4f" (abs result.CHSHTest.S)
            printfn "  Quantum bound: %.4f" result.CHSHTest.QuantumBound
            printfn "  Classical bound: %.4f" result.CHSHTest.ClassicalBound
            printfn "  Secure: %b" result.IsSecure
            printfn ""

            let keyPreview =
                result.KeyBits
                |> List.truncate 20
                |> List.map string
                |> String.concat ""
            printfn "  Key bits (first 20): %s" keyPreview
            printfn ""

        results.Add(
            [ "scenario", "secure_exchange"
              "total_pairs", string result.TotalPairs
              "sifted_key_length", string result.SiftedKeyLength
              "key_rate", sprintf "%.4f" result.KeyRate
              "chsh_s", sprintf "%.4f" (abs result.CHSHTest.S)
              "quantum_bound", sprintf "%.4f" result.CHSHTest.QuantumBound
              "classical_bound", sprintf "%.4f" result.CHSHTest.ClassicalBound
              "is_secure", string result.IsSecure ]
            |> Map.ofList)

// ============================================================================
// Scenario 3: CHSH Inequality Analysis
// ============================================================================

if runScenario "chsh" then
    if not quiet then
        printfn "--- Scenario 3: CHSH Inequality Analysis ---"
        printfn ""

    match run backend (max numPairs 300) (Some 123) with
    | Error err ->
        if not quiet then printfn "  ERROR: %A" err
        results.Add([ "scenario", "chsh_analysis"; "error", sprintf "%A" err ] |> Map.ofList)

    | Ok result ->
        if not quiet then
            printfn "%s" (formatCHSH result.CHSHTest)

        for (name, value) in result.CHSHTest.Correlations do
            results.Add(
                [ "scenario", "chsh_analysis"
                  "correlation_pair", name
                  "correlation_value", sprintf "%.4f" value
                  "chsh_s", sprintf "%.4f" (abs result.CHSHTest.S)
                  "is_secure", string result.CHSHTest.IsSecure ]
                |> Map.ofList)

// ============================================================================
// Scenario 4: Eavesdropper Detection
// ============================================================================

if runScenario "eve" then
    if not quiet then
        printfn "--- Scenario 4: Eavesdropper Detection ---"
        printfn ""
        printfn "  Running protocol WITHOUT Eve..."

    let noEveResult = run backend (max numPairs 300) (Some 99)

    if not quiet then
        printfn "  Running protocol WITH Eve (intercept-resend attack)..."

    let withEveResult = runWithEve backend (max numPairs 300) (Some 99)

    match (noEveResult, withEveResult) with
    | (Ok noEve, Ok withEve) ->
        if not quiet then
            printfn ""
            printfn "  Comparison:"
            printfn "  %-25s  %-12s  %-12s" "" "No Eve" "With Eve"
            printfn "  %-25s  %-12s  %-12s" "-------------------------" "------------" "------------"
            printfn "  %-25s  %-12.4f  %-12.4f" "|S| (CHSH parameter)" (abs noEve.CHSHTest.S) (abs withEve.CHSHTest.S)
            printfn "  %-25s  %-12s  %-12s" "Secure?" (string noEve.IsSecure) (string withEve.IsSecure)
            printfn "  %-25s  %-12d  %-12d" "Sifted key bits" noEve.SiftedKeyLength withEve.SiftedKeyLength
            printfn "  %-25s  %-12.1f  %-12.1f" "Key rate (%%)" (noEve.KeyRate * 100.0) (withEve.KeyRate * 100.0)
            printfn ""

            if noEve.IsSecure && not withEve.IsSecure then
                printfn "  Result: Eve's intercept-resend attack DETECTED!"
                printfn "  The CHSH parameter dropped below the quantum bound,"
                printfn "  indicating destroyed entanglement. Protocol aborted."
            elif noEve.IsSecure then
                printfn "  Result: Both runs appear secure (Eve's attack may have"
                printfn "  insufficient samples to show clear difference)."
            else
                printfn "  Result: Statistical fluctuations observed."
                printfn "  In production, use more pairs for reliable detection."
            printfn ""

        results.Add(
            [ "scenario", "eavesdropper_detection"
              "condition", "no_eve"
              "chsh_s", sprintf "%.4f" (abs noEve.CHSHTest.S)
              "is_secure", string noEve.IsSecure
              "sifted_key_length", string noEve.SiftedKeyLength
              "key_rate", sprintf "%.4f" noEve.KeyRate ]
            |> Map.ofList)

        results.Add(
            [ "scenario", "eavesdropper_detection"
              "condition", "with_eve"
              "chsh_s", sprintf "%.4f" (abs withEve.CHSHTest.S)
              "is_secure", string withEve.IsSecure
              "sifted_key_length", string withEve.SiftedKeyLength
              "key_rate", sprintf "%.4f" withEve.KeyRate
              "eavesdropper_detected", string withEve.CHSHTest.EavesdropperDetected ]
            |> Map.ofList)

    | (Error err, _) ->
        if not quiet then printfn "  ERROR (no Eve): %A" err
    | (_, Error err) ->
        if not quiet then printfn "  ERROR (with Eve): %A" err

// ============================================================================
// Scenario 5: Full Formatted Report
// ============================================================================

if runScenario "report" then
    if not quiet then
        printfn "--- Scenario 5: Full Protocol Report ---"
        printfn ""

    match run backend numPairs (Some 7) with
    | Error err ->
        if not quiet then printfn "  ERROR: %A" err
    | Ok result ->
        if not quiet then
            printfn "%s" (formatResult result)

        results.Add(
            [ "scenario", "full_report"
              "total_pairs", string result.TotalPairs
              "sifted_key_length", string result.SiftedKeyLength
              "key_rate", sprintf "%.4f" result.KeyRate
              "chsh_s", sprintf "%.4f" (abs result.CHSHTest.S)
              "is_secure", string result.IsSecure
              "backend", result.BackendName ]
            |> Map.ofList)

if not quiet then
    printfn "--- Protocol Summary ---"
    printfn ""
    printfn "  E91 vs BB84:"
    printfn "    BB84: prepare-and-measure (single photon encoding)"
    printfn "    E91:  entanglement-based (Bell pairs + CHSH test)"
    printfn ""
    printfn "  E91 Advantages:"
    printfn "  - Device-independent security (relies only on Bell violation)"
    printfn "  - Natural eavesdropper detection via CHSH inequality"
    printfn "  - Suitable for satellite/long-distance quantum networks"
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
    printfn "  dotnet fsi EkertQKDExample.fsx -- --pairs 500 --seed 42"
    printfn "  dotnet fsi EkertQKDExample.fsx -- --scenario eve"
    printfn "  dotnet fsi EkertQKDExample.fsx -- --quiet --output results.json --csv results.csv"
    printfn "  dotnet fsi EkertQKDExample.fsx -- --help"
