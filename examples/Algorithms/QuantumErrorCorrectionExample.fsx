// Quantum Error Correction Example
// Demonstrates four standard QEC codes with round-trip error correction
//
// Usage:
//   dotnet fsi QuantumErrorCorrectionExample.fsx
//   dotnet fsi QuantumErrorCorrectionExample.fsx -- --help
//   dotnet fsi QuantumErrorCorrectionExample.fsx -- --code shor --error-qubit 4
//   dotnet fsi QuantumErrorCorrectionExample.fsx -- --quiet --output results.json --csv results.csv

(*
===============================================================================
 Background Theory
===============================================================================

Quantum Error Correction (QEC) protects quantum information from decoherence
and noise. Unlike classical error correction, QEC faces unique challenges:
  - No-cloning theorem: Cannot copy unknown quantum states
  - Measurement collapse: Observing a qubit destroys superposition
  - Continuous errors: Errors can be arbitrary rotations, not just bit flips

The breakthrough insight is that by encoding logical qubits into entangled
multi-qubit states, we can extract error *syndromes* without measuring the
encoded information, and then apply corrections.

Notation: [[n,k,d]] = n physical qubits, k logical qubits, d code distance
Code distance d corrects floor((d-1)/2) errors.

Four codes demonstrated:
  1. BitFlip   [[3,1,1]]: Corrects single X errors
  2. PhaseFlip [[3,1,1]]: Corrects single Z errors
  3. Shor      [[9,1,3]]: Corrects any single-qubit error (first QEC code, 1995)
  4. Steane    [[7,1,3]]: CSS code, 7 qubits with transversal gates

Key Insight: The discretization theorem shows that correcting X, Z, and Y
(= iXZ) suffices to correct ALL single-qubit errors including arbitrary rotations.

Production Use Cases:
  - Fault-tolerant quantum computation
  - Quantum memory (preserving quantum states)
  - Quantum communication (channel error correction)
  - Benchmarking quantum hardware

References:
  [1] Shor, "Scheme for reducing decoherence in quantum computer memory",
      Phys. Rev. A 52, R2493 (1995).
  [2] Steane, "Error Correcting Codes in Quantum Theory",
      Phys. Rev. Lett. 77, 793 (1996).
  [3] Nielsen & Chuang, "Quantum Computation and Quantum Information",
      Cambridge University Press (2010), Chapter 10.
  [4] Wikipedia: Quantum error correction
      https://en.wikipedia.org/wiki/Quantum_error_correction
*)

#r "nuget: Microsoft.Extensions.Logging.Abstractions, 10.0.0"
#r "../../src/FSharp.Azure.Quantum/bin/Debug/net10.0/FSharp.Azure.Quantum.dll"
#load "../_common/Cli.fs"
#load "../_common/Data.fs"
#load "../_common/Reporting.fs"

open FSharp.Azure.Quantum.Algorithms.QuantumErrorCorrection
open FSharp.Azure.Quantum.Backends.LocalBackend
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Examples.Common

// ============================================================================
// CLI Setup
// ============================================================================

let argv = fsi.CommandLineArgs |> Array.skip 1
let args = Cli.parse argv

Cli.exitIfHelp "QuantumErrorCorrectionExample.fsx" "QEC codes: encode, inject error, measure syndrome, correct, verify." [
    { Name = "code"; Description = "Code to test (bitflip/phaseflip/shor/steane/all)"; Default = Some "all" }
    { Name = "error-qubit"; Description = "Qubit index for error injection (default varies by code)"; Default = None }
    { Name = "logical-bit"; Description = "Logical bit to encode (0 or 1)"; Default = Some "0" }
    { Name = "output"; Description = "Write results to JSON file"; Default = None }
    { Name = "csv"; Description = "Write results to CSV file"; Default = None }
    { Name = "quiet"; Description = "Suppress informational output"; Default = None }
] args

let codeArg = Cli.getOr "code" "all" args
let errorQubitOverride = Cli.tryGet "error-qubit" args |> Option.map int
let logicalBit = Cli.getIntOr "logical-bit" 0 args
let quiet = Cli.hasFlag "quiet" args
let outputPath = Cli.tryGet "output" args
let csvPath = Cli.tryGet "csv" args

let runCode name = codeArg = "all" || codeArg = name

// ============================================================================
// Quantum Backend
// ============================================================================

let backend = LocalBackend() :> IQuantumBackend

// ============================================================================
// Result Collection
// ============================================================================

let results = System.Collections.Generic.List<Map<string, string>>()

let addRoundTripResult (codeName: string) (errorType: string) (errorQubit: int) (r: RoundTripResult) =
    results.Add(
        [ "code", codeName
          "logical_bit", string r.LogicalBit
          "error_type", errorType
          "error_qubit", string errorQubit
          "syndrome", (r.Syndrome.SyndromeBits |> List.map string |> String.concat ",")
          "correction_applied", string r.CorrectionApplied
          "decoded_bit", string r.DecodedBit
          "success", string r.Success
          "backend", r.BackendName ]
        |> Map.ofList)

// ============================================================================
// Scenario 1: Code Parameters
// ============================================================================

if not quiet then
    printfn "=== Quantum Error Correction ==="
    printfn ""
    printfn "BUSINESS SCENARIO:"
    printfn "Protect quantum information from noise and decoherence using"
    printfn "multi-qubit encoding with syndrome-based error correction."
    printfn ""
    printfn "Backend: %s" backend.Name
    printfn ""
    printfn "--- Code Parameters ---"
    printfn ""

let codes = [ BitFlipCode3; PhaseFlipCode3; ShorCode9; SteaneCode7 ]

for code in codes do
    let p = codeParameters code
    let codeName =
        match code with
        | BitFlipCode3 -> "BitFlip"
        | PhaseFlipCode3 -> "PhaseFlip"
        | ShorCode9 -> "Shor"
        | SteaneCode7 -> "Steane"

    if not quiet then
        printfn "  %s" (formatCodeParameters code)

    results.Add(
        [ "scenario", "parameters"
          "code", codeName
          "physical_qubits", string p.PhysicalQubits
          "logical_qubits", string p.LogicalQubits
          "distance", string p.Distance
          "correctable_errors", string p.CorrectableErrors ]
        |> Map.ofList)

if not quiet then printfn ""

// ============================================================================
// Scenario 2: Bit-Flip Code [[3,1,1]]
// ============================================================================

if runCode "bitflip" then
    if not quiet then
        printfn "--- Bit-Flip Code [[3,1,1]] ---"
        printfn "  Encoding: |0> -> |000>,  |1> -> |111>"
        printfn "  Corrects: Single X (bit-flip) errors"
        printfn ""

    // No error
    match BitFlip.roundTrip backend logicalBit None with
    | Error err ->
        if not quiet then printfn "  ERROR: %A" err
    | Ok r ->
        if not quiet then
            printfn "  No error:   encoded |%d> -> decoded |%d>  [%s]"
                r.LogicalBit r.DecodedBit (if r.Success then "OK" else "FAIL")
        addRoundTripResult "BitFlip" "none" -1 r

    // Error on each data qubit
    let testQubits = match errorQubitOverride with Some q -> [q] | None -> [0; 1; 2]
    for q in testQubits do
        for lb in [0; 1] do
            match BitFlip.roundTrip backend lb (Some q) with
            | Error err ->
                if not quiet then printfn "  ERROR: %A" err
            | Ok r ->
                if not quiet then
                    printfn "  X on q%d:    encoded |%d> -> syndrome %A -> decoded |%d>  [%s]"
                        q r.LogicalBit r.Syndrome.SyndromeBits r.DecodedBit
                        (if r.Success then "OK" else "FAIL")
                addRoundTripResult "BitFlip" "X" q r

    if not quiet then printfn ""

// ============================================================================
// Scenario 3: Phase-Flip Code [[3,1,1]]
// ============================================================================

if runCode "phaseflip" then
    if not quiet then
        printfn "--- Phase-Flip Code [[3,1,1]] ---"
        printfn "  Encoding: |0> -> |+++>,  |1> -> |--->"
        printfn "  Corrects: Single Z (phase-flip) errors"
        printfn ""

    let testQubits = match errorQubitOverride with Some q -> [q] | None -> [0; 1; 2]
    for q in testQubits do
        for lb in [0; 1] do
            match PhaseFlip.roundTrip backend lb (Some q) with
            | Error err ->
                if not quiet then printfn "  ERROR: %A" err
            | Ok r ->
                if not quiet then
                    printfn "  Z on q%d:    encoded |%d> -> syndrome %A -> decoded |%d>  [%s]"
                        q r.LogicalBit r.Syndrome.SyndromeBits r.DecodedBit
                        (if r.Success then "OK" else "FAIL")
                addRoundTripResult "PhaseFlip" "Z" q r

    if not quiet then printfn ""

// ============================================================================
// Scenario 4: Shor 9-Qubit Code [[9,1,3]]
// ============================================================================

if runCode "shor" then
    if not quiet then
        printfn "--- Shor 9-Qubit Code [[9,1,3]] ---"
        printfn "  First QEC code (Shor, 1995). Concatenation of phase-flip"
        printfn "  (outer) and bit-flip (inner) codes."
        printfn "  Corrects ANY single-qubit error: X, Z, or Y = iXZ."
        printfn ""

    let errQubit = errorQubitOverride |> Option.defaultValue 1

    for (errType, errLabel) in [(BitFlipError, "X"); (PhaseFlipError, "Z"); (CombinedError, "Y")] do
        for lb in [0; 1] do
            match Shor.roundTrip backend lb errType errQubit with
            | Error err ->
                if not quiet then printfn "  ERROR: %A" err
            | Ok r ->
                if not quiet then
                    printfn "  %s on q%d:    encoded |%d> -> decoded |%d>  [%s]"
                        errLabel errQubit r.LogicalBit r.DecodedBit
                        (if r.Success then "OK" else "FAIL")
                addRoundTripResult "Shor" errLabel errQubit r

    if not quiet then printfn ""

// ============================================================================
// Scenario 5: Steane 7-Qubit Code [[7,1,3]]
// ============================================================================

if runCode "steane" then
    if not quiet then
        printfn "--- Steane 7-Qubit Code [[7,1,3]] ---"
        printfn "  CSS code based on classical [7,4,3] Hamming code."
        printfn "  Uses only 7 qubits (vs 9 for Shor) with transversal gates."
        printfn ""

    let errQubit = errorQubitOverride |> Option.defaultValue 3

    for (errType, errLabel) in [(BitFlipError, "X"); (PhaseFlipError, "Z"); (CombinedError, "Y")] do
        for lb in [0; 1] do
            match Steane.roundTrip backend lb errType errQubit with
            | Error err ->
                if not quiet then printfn "  ERROR: %A" err
            | Ok r ->
                if not quiet then
                    printfn "  %s on q%d:    encoded |%d> -> decoded |%d>  [%s]"
                        errLabel errQubit r.LogicalBit r.DecodedBit
                        (if r.Success then "OK" else "FAIL")
                addRoundTripResult "Steane" errLabel errQubit r

    if not quiet then printfn ""

// ============================================================================
// Summary: Code Comparison
// ============================================================================

if not quiet then
    printfn "--- Code Comparison ---"
    printfn ""
    printfn "  %-20s  %-8s  %-8s  %-10s  %-12s" "Code" "Qubits" "Dist." "Corrects" "Error Types"
    printfn "  %-20s  %-8s  %-8s  %-10s  %-12s" "--------------------" "--------" "--------" "----------" "------------"

    let codeInfo = [
        (BitFlipCode3,   "X only")
        (PhaseFlipCode3, "Z only")
        (ShorCode9,      "X, Z, Y")
        (SteaneCode7,    "X, Z, Y")
    ]

    for (code, errorTypes) in codeInfo do
        let p = codeParameters code
        let name =
            match code with
            | BitFlipCode3 -> "Bit-Flip [[3,1,1]]"
            | PhaseFlipCode3 -> "Phase-Flip [[3,1,1]]"
            | ShorCode9 -> "Shor [[9,1,3]]"
            | SteaneCode7 -> "Steane [[7,1,3]]"
        printfn "  %-20s  %-8d  %-8d  %-10d  %-12s"
            name p.PhysicalQubits p.Distance p.CorrectableErrors errorTypes

    printfn ""
    printfn "  Key insight: Correcting {X, Z, Y} covers ALL single-qubit errors"
    printfn "  (discretization theorem). Shor and Steane codes achieve this."
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
    printfn "  dotnet fsi QuantumErrorCorrectionExample.fsx -- --code shor --error-qubit 4"
    printfn "  dotnet fsi QuantumErrorCorrectionExample.fsx -- --code steane --logical-bit 1"
    printfn "  dotnet fsi QuantumErrorCorrectionExample.fsx -- --quiet --output results.json --csv results.csv"
    printfn "  dotnet fsi QuantumErrorCorrectionExample.fsx -- --help"
