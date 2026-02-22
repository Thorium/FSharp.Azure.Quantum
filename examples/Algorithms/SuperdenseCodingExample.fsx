// Superdense Coding Protocol Example
// Demonstrates quantum communication: send 2 classical bits via 1 qubit + entanglement
//
// Usage:
//   dotnet fsi SuperdenseCodingExample.fsx
//   dotnet fsi SuperdenseCodingExample.fsx -- --help
//   dotnet fsi SuperdenseCodingExample.fsx -- --message 11 --trials 200
//   dotnet fsi SuperdenseCodingExample.fsx -- --quiet --output results.json --csv results.csv

(*
===============================================================================
 Background Theory
===============================================================================

Superdense coding (also called "dense coding") was proposed by Bennett and
Wiesner in 1992 and experimentally demonstrated by Mattle et al. in 1996.

The protocol allows Alice to send 2 classical bits of information to Bob by
transmitting only 1 qubit, provided they share a pre-existing entangled Bell
pair |Phi+> = (|00> + |11>) / sqrt(2).

Protocol Steps:
  1. Preparation: Alice and Bob share a Bell pair |Phi+>
  2. Encoding: Alice applies one of 4 operations to her qubit:
     - 00 -> I (identity):   |Phi+> -> |Phi+> = (|00> + |11>) / sqrt(2)
     - 01 -> X (bit flip):   |Phi+> -> |Psi+> = (|01> + |10>) / sqrt(2)
     - 10 -> Z (phase flip): |Phi+> -> |Phi-> = (|00> - |11>) / sqrt(2)
     - 11 -> ZX (both):      |Phi+> -> |Psi-> = (|01> - |10>) / sqrt(2)
  3. Transmission: Alice sends her qubit to Bob (1 qubit carries 2 bits!)
  4. Decoding: Bob performs Bell measurement (CNOT -> H -> Measure)
  5. Result: Bob recovers the 2 classical bits

Key Equations:
  - Holevo bound: 1 qubit can carry at most 1 classical bit (without entanglement)
  - With pre-shared entanglement: 1 qubit can carry 2 classical bits
  - This does NOT violate Holevo's theorem (entanglement is pre-shared resource)

Quantum Advantage:
  Superdense coding doubles classical channel capacity using pre-shared
  entanglement. It is the dual of quantum teleportation:
  - Teleportation: 1 qubit of quantum info via 2 classical bits + 1 ebit
  - Superdense:    2 classical bits via 1 qubit + 1 ebit

References:
  [1] Bennett, Wiesner, "Communication via one- and two-particle operators on
      Einstein-Podolsky-Rosen states", Phys. Rev. Lett. 69, 2881 (1992).
  [2] Mattle et al., "Dense Coding in Experimental Quantum Communication",
      Phys. Rev. Lett. 76, 4656 (1996).
  [3] Nielsen & Chuang, "Quantum Computation and Quantum Information",
      Cambridge University Press (2010), Section 2.3.
  [4] Wikipedia: Superdense_coding
      https://en.wikipedia.org/wiki/Superdense_coding
*)

#r "nuget: Microsoft.Extensions.Logging.Abstractions, 10.0.0"
#r "../../src/FSharp.Azure.Quantum/bin/Debug/net10.0/FSharp.Azure.Quantum.dll"
#load "../_common/Cli.fs"
#load "../_common/Data.fs"
#load "../_common/Reporting.fs"

open FSharp.Azure.Quantum.Algorithms.SuperdenseCoding
open FSharp.Azure.Quantum.Backends.LocalBackend
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Examples.Common

// ============================================================================
// CLI Setup
// ============================================================================

let argv = fsi.CommandLineArgs |> Array.skip 1
let args = Cli.parse argv

Cli.exitIfHelp "SuperdenseCodingExample.fsx" "Superdense coding: send 2 classical bits via 1 qubit + entanglement." [
    { Name = "message"; Description = "2-bit message to send (00, 01, 10, 11)"; Default = Some "all" }
    { Name = "trials"; Description = "Statistical verification trials"; Default = Some "100" }
    { Name = "output"; Description = "Write results to JSON file"; Default = None }
    { Name = "csv"; Description = "Write results to CSV file"; Default = None }
    { Name = "quiet"; Description = "Suppress informational output"; Default = None }
] args

let messageArg = Cli.getOr "message" "all" args
let trials = Cli.getIntOr "trials" 100 args
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

// ============================================================================
// Scenario 1: Send All 4 Message Encodings
// ============================================================================

let allMessages : ClassicalMessage list = [
    { Bit1 = 0; Bit2 = 0 }
    { Bit1 = 0; Bit2 = 1 }
    { Bit1 = 1; Bit2 = 0 }
    { Bit1 = 1; Bit2 = 1 }
]

let messagesToTest =
    match messageArg with
    | "all" -> allMessages
    | s when s.Length = 2 ->
        let b1 = int (string s.[0])
        let b2 = int (string s.[1])
        [ { Bit1 = b1; Bit2 = b2 } ]
    | _ -> allMessages

if not quiet then
    printfn "=== Superdense Coding Protocol ==="
    printfn ""
    printfn "BUSINESS SCENARIO:"
    printfn "Quantum communication channel that doubles classical capacity"
    printfn "by using pre-shared entanglement (Bell pairs)."
    printfn ""
    printfn "Backend: %s" backend.Name
    printfn ""
    printfn "--- Message Transmission ---"
    printfn ""

for msg in messagesToTest do
    let encoding =
        match (msg.Bit1, msg.Bit2) with
        | (0, 0) -> "I (identity)"
        | (0, 1) -> "X (bit flip)"
        | (1, 0) -> "Z (phase flip)"
        | (1, 1) -> "ZX (both)"
        | _ -> "?"

    match send backend msg with
    | Error err ->
        if not quiet then
            printfn "  ERROR sending %d%d: %A" msg.Bit1 msg.Bit2 err

        results.Add(
            [ "scenario", "send"
              "sent", sprintf "%d%d" msg.Bit1 msg.Bit2
              "encoding", encoding
              "received", ""
              "success", "false"
              "error", sprintf "%A" err ]
            |> Map.ofList)

    | Ok result ->
        let success = result.Success

        if not quiet then
            let status = if success then "OK" else "FAIL"
            printfn "  Send %d%d [%-15s] -> Received %d%d  [%s]"
                msg.Bit1 msg.Bit2 encoding
                result.ReceivedMessage.Bit1 result.ReceivedMessage.Bit2
                status

        results.Add(
            [ "scenario", "send"
              "sent", sprintf "%d%d" msg.Bit1 msg.Bit2
              "encoding", encoding
              "received", sprintf "%d%d" result.ReceivedMessage.Bit1 result.ReceivedMessage.Bit2
              "success", string success
              "error", "" ]
            |> Map.ofList)

if not quiet then printfn ""

// ============================================================================
// Scenario 2: Statistical Verification
// ============================================================================

if not quiet then
    printfn "--- Statistical Verification (%d trials per message) ---" trials
    printfn ""

for msg in messagesToTest do
    match runStatistics backend msg trials with
    | Error err ->
        if not quiet then
            printfn "  ERROR for %d%d: %A" msg.Bit1 msg.Bit2 err

    | Ok stats ->
        if not quiet then
            printfn "  Message %d%d: %d/%d correct (%.1f%% accuracy)"
                msg.Bit1 msg.Bit2
                stats.SuccessCount stats.TotalTrials
                (float stats.SuccessCount / float stats.TotalTrials * 100.0)

        results.Add(
            [ "scenario", "statistics"
              "message", sprintf "%d%d" msg.Bit1 msg.Bit2
              "trials", string stats.TotalTrials
              "correct", string stats.SuccessCount
              "accuracy", sprintf "%.4f" (float stats.SuccessCount / float stats.TotalTrials)
              "success", string (stats.SuccessCount = stats.TotalTrials) ]
            |> Map.ofList)

if not quiet then
    printfn ""

    // Protocol summary
    printfn "--- Protocol Summary ---"
    printfn ""
    printfn "  Classical channel capacity: 1 bit per qubit"
    printfn "  Superdense coding capacity: 2 bits per qubit (+ 1 ebit)"
    printfn "  Capacity improvement:       2x"
    printfn ""
    printfn "  Applications:"
    printfn "  - Quantum networks (efficient classical data over quantum channels)"
    printfn "  - Quantum internet protocols (bandwidth optimization)"
    printfn "  - Fundamental test of quantum entanglement as a resource"
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
    printfn "  dotnet fsi SuperdenseCodingExample.fsx -- --message 11 --trials 200"
    printfn "  dotnet fsi SuperdenseCodingExample.fsx -- --quiet --output results.json --csv results.csv"
    printfn "  dotnet fsi SuperdenseCodingExample.fsx -- --help"
