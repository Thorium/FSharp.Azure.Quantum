/// Quantum Teleportation Protocol Example
/// 
/// Demonstrates the canonical quantum teleportation protocol:
/// - Transfer quantum state from Alice to Bob using entanglement
/// - Uses pre-shared Bell pair + 2 classical bits
/// - Original state destroyed (no-cloning theorem)
/// 
/// **Textbook References**:
/// - Nielsen & Chuang "Quantum Computation and Quantum Information" - Section 1.3.7
/// - "Learn Quantum Computing with Python and Q#" (Kaiser, 2021) - Chapter 8
/// - "Quantum Programming in Depth" (Manning, 2024) - Chapter 10
/// 
/// **Production Use Cases**:
/// - Quantum Networks (transfer states between nodes)
/// - Quantum Repeaters (extend communication range)
/// - Distributed Quantum Computing (move data between processors)
/// 
/// **Real-World Deployments**:
/// - Micius satellite: 1400 km teleportation (2017)
/// - USTC China: 143 km fiber teleportation (2012)
/// - Delft quantum network experiments (2022)

(*
===============================================================================
 Background Theory
===============================================================================

Quantum teleportation (Bennett et al., 1993) transfers an unknown quantum state
|ÏˆâŸ© = Î±|0âŸ© + Î²|1âŸ© from Alice to Bob using shared entanglement and classical
communication. Crucially, the protocol does NOT transmit the physical qubit or
violate relativity (classical bits must be sent). Instead, it "disassembles"
the quantum information at Alice's location and "reassembles" it at Bob's,
consuming one Bell pair and two classical bits per teleported qubit.

The protocol proceeds in three steps: (1) Alice and Bob pre-share a Bell state
|Î¦âºâŸ© = (|00âŸ© + |11âŸ©)/âˆš2. (2) Alice performs a Bell-basis measurement on her
qubit (the state to teleport) and her half of the Bell pair, obtaining 2 classical
bits (mâ‚, mâ‚‚). This measurement collapses Bob's qubit into one of four states,
each related to |ÏˆâŸ© by a known Pauli operation. (3) Alice sends (mâ‚, mâ‚‚) to Bob,
who applies the corresponding correction: I, X, Z, or XZ. Bob now has |ÏˆâŸ©.

Key Equations:
  - Initial state: |ÏˆâŸ©_A âŠ— |Î¦âºâŸ©_AB = (Î±|0âŸ© + Î²|1âŸ©) âŠ— (|00âŸ© + |11âŸ©)/âˆš2
  - Bell measurement outcomes and Bob's state:
    |Î¦âºâŸ©: Bob has |ÏˆâŸ© (apply I)
    |Î¦â»âŸ©: Bob has Z|ÏˆâŸ© (apply Z)
    |Î¨âºâŸ©: Bob has X|ÏˆâŸ© (apply X)
    |Î¨â»âŸ©: Bob has XZ|ÏˆâŸ© (apply XZ)
  - Resource cost: 1 ebit (Bell pair) + 2 cbits â†’ 1 teleported qubit
  - Fidelity: F = |âŸ¨Ïˆ_ideal|Ïˆ_actualâŸ©|Â² (ideally 1.0, limited by noise)

Quantum Advantage:
  Teleportation is foundational for quantum networking and distributed quantum
  computing. It enables: (1) Quantum repeaters that extend entanglement over
  long distances via entanglement swapping. (2) Gate teleportation for fault-
  tolerant quantum computing (teleporting through a gate). (3) Quantum internet
  protocols where qubits cannot be directly transmitted. The no-cloning theorem
  ensures the original state is destroyed, preserving unitarity.

References:
  [1] Bennett et al., "Teleporting an unknown quantum state via dual classical
      and Einstein-Podolsky-Rosen channels", Phys. Rev. Lett. 70, 1895 (1993).
      https://doi.org/10.1103/PhysRevLett.70.1895
  [2] Bouwmeester et al., "Experimental quantum teleportation", Nature 390,
      575-579 (1997). https://doi.org/10.1038/37539
  [3] Ren et al., "Ground-to-satellite quantum teleportation", Nature 549,
      70-73 (2017). https://doi.org/10.1038/nature23675
  [4] Wikipedia: Quantum_teleportation
      https://en.wikipedia.org/wiki/Quantum_teleportation
*)

//#r "nuget: FSharp.Azure.Quantum"
#r "nuget: Microsoft.Extensions.Logging.Abstractions, 10.0.0"
#r "../../src/FSharp.Azure.Quantum/bin/Debug/net10.0/FSharp.Azure.Quantum.dll"

#load "../_common/Cli.fs"
#load "../_common/Data.fs"
#load "../_common/Reporting.fs"

open System
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.CircuitBuilder
open FSharp.Azure.Quantum.Algorithms.QuantumTeleportation
open FSharp.Azure.Quantum.Backends.LocalBackend
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Examples.Common

// ---------------------------------------------------------------------------
// CLI
// ---------------------------------------------------------------------------

let argv = fsi.CommandLineArgs |> Array.skip 1
let args = Cli.parse argv

Cli.exitIfHelp
    "QuantumTeleportationExample.fsx"
    "Demonstrate quantum teleportation of various quantum states."
    [ { Cli.OptionSpec.Name = "state"
        Description = "State to teleport: zero|one|plus|minus|stats|all"
        Default = Some "all" }
      { Cli.OptionSpec.Name = "runs"
        Description = "Number of runs for statistics test"
        Default = Some "20" }
      { Cli.OptionSpec.Name = "output"
        Description = "Write results to JSON file"
        Default = None }
      { Cli.OptionSpec.Name = "csv"
        Description = "Write results to CSV file"
        Default = None }
      { Cli.OptionSpec.Name = "quiet"
        Description = "Suppress printed output"
        Default = None } ]
    args

let quiet = Cli.hasFlag "quiet" args
let stateName = Cli.getOr "state" "all" args
let numRuns = Cli.getIntOr "runs" 20 args
let outputPath = Cli.tryGet "output" args
let csvPath = Cli.tryGet "csv" args

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

let backend = LocalBackend() :> IQuantumBackend

let results = ResizeArray<Map<string, string>>()
let shouldRun name = stateName = "all" || stateName = name

/// Build a structured result row from a teleportation result.
let resultRow (test: string) (result: TeleportationResult) : Map<string, string> =
    [ "test", test
      "alice_measurement", sprintf "%A" result.AliceMeasurement
      "bob_correction", sprintf "%A" result.BobCorrection
      "fidelity", sprintf "%.4f" result.Fidelity
      "num_qubits", string result.NumQubits
      "backend", result.BackendName ]
    |> Map.ofList

// ---------------------------------------------------------------------------
// Protocol overview
// ---------------------------------------------------------------------------

if not quiet then
    printfn "============================================================"
    printfn "  Quantum Teleportation Protocol Demo"
    printfn "============================================================"
    printfn ""
    printfn "Protocol Overview"
    printfn "-----------------------------------------------------------"
    printfn "Alice wants to send quantum state to Bob:"
    printfn "  1. Alice & Bob share entangled Bell pair"
    printfn "  2. Alice entangles her state with her Bell qubit"
    printfn "  3. Alice measures her qubits -> 2 classical bits"
    printfn "  4. Alice sends classical bits to Bob"
    printfn "  5. Bob applies corrections based on classical bits"
    printfn "  6. Bob now has original state (Alice's destroyed)"
    printfn ""
    printfn "Resources: 3 qubits, 1 Bell pair, 2 classical bits, ~4 gates"
    printfn ""

// ---------------------------------------------------------------------------
// Test 1: Teleport |0> State
// ---------------------------------------------------------------------------

if shouldRun "zero" then
    if not quiet then
        printfn "Test 1: Teleporting |0> State"
        printfn "-----------------------------------------------------------"

    match teleportZero backend with
    | Ok result ->
        if not quiet then
            printfn "%s" (formatResult result)
            printfn ""
        results.Add(resultRow "zero" result)
    | Error err ->
        if not quiet then printfn "  Error: %A" err
        printfn ""

// ---------------------------------------------------------------------------
// Test 2: Teleport |1> State
// ---------------------------------------------------------------------------

if shouldRun "one" then
    if not quiet then
        printfn "Test 2: Teleporting |1> State"
        printfn "-----------------------------------------------------------"

    match teleportOne backend with
    | Ok result ->
        if not quiet then
            printfn "%s" (formatResult result)
            printfn ""
        results.Add(resultRow "one" result)
    | Error err ->
        if not quiet then printfn "  Error: %A" err
        printfn ""

// ---------------------------------------------------------------------------
// Test 3: Teleport |+> State (Superposition)
// ---------------------------------------------------------------------------

if shouldRun "plus" then
    if not quiet then
        printfn "Test 3: Teleporting |+> State (Superposition)"
        printfn "-----------------------------------------------------------"
        printfn "Input:  |+> = (|0> + |1>)/sqrt(2)"
        printfn ""

    match teleportPlus backend with
    | Ok result ->
        if not quiet then
            printfn "%s" (formatResult result)
            printfn ""
        results.Add(resultRow "plus" result)
    | Error err ->
        if not quiet then printfn "  Error: %A" err
        printfn ""

// ---------------------------------------------------------------------------
// Test 4: Teleport |-> State (Phase)
// ---------------------------------------------------------------------------

if shouldRun "minus" then
    if not quiet then
        printfn "Test 4: Teleporting |-> State (Phase)"
        printfn "-----------------------------------------------------------"
        printfn "Input:  |-> = (|0> - |1>)/sqrt(2)"
        printfn ""

    match teleportMinus backend with
    | Ok result ->
        if not quiet then
            printfn "%s" (formatResult result)
            printfn ""
        results.Add(resultRow "minus" result)
    | Error err ->
        if not quiet then printfn "  Error: %A" err
        printfn ""

// ---------------------------------------------------------------------------
// Test 5: Statistical Analysis (Multiple Runs)
// ---------------------------------------------------------------------------

if shouldRun "stats" then
    if not quiet then
        printfn "Test 5: Statistical Analysis (%d runs)" numRuns
        printfn "-----------------------------------------------------------"

    let prepareInputState (b: IQuantumBackend) =
        result {
            let! state = b.InitializeState 3
            return! b.ApplyOperation (QuantumOperation.Gate (H 0)) state
        }

    match runStatistics prepareInputState backend numRuns with
    | Ok statsResults ->
        if not quiet then
            printfn "%s" (analyzeStatistics statsResults)
            printfn ""

        for (i, r) in statsResults |> List.mapi (fun i r -> (i, r)) do
            results.Add(resultRow (sprintf "stats_run_%d" (i + 1)) r)
    | Error err ->
        if not quiet then printfn "  Error: %A" err
        printfn ""

// ---------------------------------------------------------------------------
// Structured output
// ---------------------------------------------------------------------------

let resultsList = results |> Seq.toList

match outputPath with
| Some p -> Reporting.writeJson p resultsList
| None -> ()

match csvPath with
| Some p ->
    let header = ["test"; "alice_measurement"; "bob_correction"; "fidelity"; "num_qubits"; "backend"]
    let rows =
        resultsList
        |> List.map (fun m ->
            header |> List.map (fun h -> m |> Map.tryFind h |> Option.defaultValue ""))
    Reporting.writeCsv p header rows
| None -> ()

if not quiet then
    printfn "============================================================"
    printfn "  Quantum Teleportation Examples Complete!"
    printfn "============================================================"

if argv.Length = 0 then
    printfn ""
    printfn "Tip: run with --help for CLI options."
