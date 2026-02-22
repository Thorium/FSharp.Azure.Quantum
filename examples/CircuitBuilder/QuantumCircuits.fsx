#!/usr/bin/env dotnet fsi
// ============================================================================
// Quantum Circuit Builder - Computation Expression Examples
// ============================================================================
//
// Demonstrates the circuit { } computation expression for declarative quantum
// circuit construction. Includes Bell state, GHZ, QFT, superposition, phase
// kickback, Toffoli, circuit optimization, and validation.
//
// Extensible starting point for building quantum circuits programmatically.
//
// ============================================================================

#r "nuget: Microsoft.Extensions.Logging.Abstractions, 10.0.0"
#r "../../src/FSharp.Azure.Quantum/bin/Debug/net10.0/FSharp.Azure.Quantum.dll"
#load "../_common/Cli.fs"
#load "../_common/Data.fs"
#load "../_common/Reporting.fs"

open System
open FSharp.Azure.Quantum.CircuitBuilder
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Backends.LocalBackend
open FSharp.Azure.Quantum.Examples.Common

// IQuantumBackend available for downstream circuit execution
let quantumBackend = LocalBackend() :> IQuantumBackend

// --- CLI ---
let argv = fsi.CommandLineArgs |> Array.skip 1
let args = Cli.parse argv

Cli.exitIfHelp
    "QuantumCircuits.fsx"
    "Quantum circuit builder CE examples: Bell, GHZ, QFT, optimization, validation"
    [ { Name = "example"; Description = "Which example (all|bell|ghz|qft|super|kickback|toffoli|optimize|validate)"; Default = Some "all" }
      { Name = "ghz-qubits"; Description = "Number of qubits for GHZ state"; Default = Some "5" }
      { Name = "super-qubits"; Description = "Number of qubits for superposition"; Default = Some "8" }
      { Name = "output"; Description = "Write results to JSON file"; Default = None }
      { Name = "csv"; Description = "Write results to CSV file"; Default = None }
      { Name = "quiet"; Description = "Suppress console output"; Default = None } ]
    args

let quiet = Cli.hasFlag "quiet" args
let outputPath = Cli.tryGet "output" args
let csvPath = Cli.tryGet "csv" args
let exampleArg = Cli.getOr "example" "all" args
let ghzQubits = Cli.getIntOr "ghz-qubits" 5 args
let superQubits = Cli.getIntOr "super-qubits" 8 args

let pr fmt = Printf.ksprintf (fun s -> if not quiet then printfn "%s" s) fmt

// --- Circuit Result Type ---

type CircuitResult =
    { Name: string
      Label: string
      Qubits: int
      Gates: int
      Note: string
      Qasm: string option }

// --- Helper: run example and collect result ---

let shouldRun key = exampleArg = "all" || exampleArg = key

let mutable jsonResults : CircuitResult list = []
let mutable csvRows : string list list = []

let record (r: CircuitResult) =
    jsonResults <- jsonResults @ [ r ]
    csvRows <- csvRows @ [ [ r.Name; r.Label; string r.Qubits; string r.Gates; r.Note ] ]

// ============================================================================
// EXAMPLE 1: Bell State
// ============================================================================

if shouldRun "bell" then
    pr "=== Example 1: Bell State ==="
    pr ""

    let bellState = circuit {
        qubits 2
        H 0
        CNOT (0, 1)
    }

    let qasm = toOpenQASM bellState

    pr "  Qubits: %d  |  Gates: %d" bellState.QubitCount (List.length bellState.Gates)
    pr "  Depth: 2 (H followed by CNOT)"
    pr ""
    pr "OpenQASM Output:"
    pr "%s" qasm

    record
        { Name = "bell"
          Label = "Bell State"
          Qubits = bellState.QubitCount
          Gates = List.length bellState.Gates
          Note = "Maximally entangled pair |00>+|11>"
          Qasm = Some qasm }

// ============================================================================
// EXAMPLE 2: GHZ State
// ============================================================================

if shouldRun "ghz" then
    pr "=== Example 2: GHZ State (%d qubits) ===" ghzQubits
    pr ""

    let ghzState = circuit {
        qubits ghzQubits
        H 0
        for i in [0..ghzQubits-2] do
            yield! singleGate (Gate.CNOT (i, i+1))
    }

    pr "  Qubits: %d  |  Gates: %d (1 H + %d CNOTs)" ghzState.QubitCount (List.length ghzState.Gates) (ghzQubits - 1)
    pr ""

    record
        { Name = "ghz"
          Label = sprintf "GHZ State (%d qubits)" ghzQubits
          Qubits = ghzState.QubitCount
          Gates = List.length ghzState.Gates
          Note = sprintf "|00...0>+|11...1> over %d qubits" ghzQubits
          Qasm = None }

// ============================================================================
// EXAMPLE 3: Quantum Fourier Transform (3 qubits)
// ============================================================================

if shouldRun "qft" then
    pr "=== Example 3: Quantum Fourier Transform (3 qubits) ==="
    pr ""

    let qft3 = circuit {
        qubits 3
        H 0
        CP (1, 0, Math.PI / 2.0)
        CP (2, 0, Math.PI / 4.0)
        H 1
        CP (2, 1, Math.PI / 2.0)
        H 2
        SWAP (0, 2)
    }

    pr "  Qubits: %d  |  Gates: %d" qft3.QubitCount (List.length qft3.Gates)
    pr "  Structure: H + CP gates + final SWAP"
    pr ""

    record
        { Name = "qft"
          Label = "QFT-3"
          Qubits = qft3.QubitCount
          Gates = List.length qft3.Gates
          Note = "Quantum Fourier Transform with bit-reversal"
          Qasm = None }

// ============================================================================
// EXAMPLE 4: Uniform Superposition
// ============================================================================

if shouldRun "super" then
    pr "=== Example 4: Uniform Superposition (%d qubits) ===" superQubits
    pr ""

    let superposition = circuit {
        qubits superQubits
        for q in [0..superQubits-1] do
            yield! singleGate (Gate.H q)
    }

    pr "  Qubits: %d  |  Gates: %d Hadamards" superposition.QubitCount (List.length superposition.Gates)
    pr "  Result: Uniform superposition over 2^%d = %d basis states" superQubits (pown 2 superQubits)
    pr ""

    record
        { Name = "super"
          Label = sprintf "Superposition (%d qubits)" superQubits
          Qubits = superposition.QubitCount
          Gates = List.length superposition.Gates
          Note = sprintf "Uniform over %d basis states" (pown 2 superQubits)
          Qasm = None }

// ============================================================================
// EXAMPLE 5: Phase Kickback
// ============================================================================

if shouldRun "kickback" then
    pr "=== Example 5: Phase Kickback Demo ==="
    pr ""

    let phaseKickback = circuit {
        qubits 2
        H 0
        X 1
        H 1
        CZ (0, 1)
        H 0
    }

    pr "  Qubits: %d  |  Gates: %d" phaseKickback.QubitCount (List.length phaseKickback.Gates)
    pr "  Purpose: Demonstrates phase kickback mechanism"
    pr ""

    record
        { Name = "kickback"
          Label = "Phase Kickback"
          Qubits = phaseKickback.QubitCount
          Gates = List.length phaseKickback.Gates
          Note = "Phase kickback via controlled-Z"
          Qasm = None }

// ============================================================================
// EXAMPLE 6: Toffoli Gate (CCX)
// ============================================================================

if shouldRun "toffoli" then
    pr "=== Example 6: Toffoli Gate (CCX) ==="
    pr ""

    let toffoliDemo = circuit {
        qubits 3
        X 0
        X 1
        CCX (0, 1, 2)
    }

    pr "  Qubits: %d  |  Gates: %d" toffoliDemo.QubitCount (List.length toffoliDemo.Gates)
    pr "  Effect: Target qubit flipped only if both controls are |1>"
    pr ""

    record
        { Name = "toffoli"
          Label = "Toffoli (CCX)"
          Qubits = toffoliDemo.QubitCount
          Gates = List.length toffoliDemo.Gates
          Note = "Universal for classical reversible computation"
          Qasm = None }

// ============================================================================
// EXAMPLE 7: Circuit Optimization
// ============================================================================

if shouldRun "optimize" then
    pr "=== Example 7: Circuit Optimization ==="
    pr ""

    let unoptimized = circuit {
        qubits 2
        H 0
        H 0    // Double H cancels
        X 1
        X 1    // Double X cancels
        S 0
        SDG 0  // S followed by S-dagger cancels
    }

    let optimized = optimize unoptimized

    let beforeGates = List.length unoptimized.Gates
    let afterGates = List.length optimized.Gates

    pr "  Before optimization: %d gates" beforeGates
    pr "  After optimization:  %d gates" afterGates
    pr "  Removed: %d inverse gate pairs (H-H, X-X, S-SDG)" (beforeGates - afterGates)
    pr ""

    record
        { Name = "optimize"
          Label = "Circuit Optimization"
          Qubits = unoptimized.QubitCount
          Gates = beforeGates
          Note = sprintf "Reduced from %d to %d gates" beforeGates afterGates
          Qasm = None }

// ============================================================================
// EXAMPLE 8: Validation (Invalid Circuit Detection)
// ============================================================================

if shouldRun "validate" then
    pr "=== Example 8: Automatic Validation ==="
    pr ""

    let validationResult =
        try
            let _invalid = circuit {
                qubits 2
                H 5  // Out of bounds: only qubits 0-1
            }
            Error "Expected validation error but circuit was accepted"
        with ex ->
            pr "  [OK] Caught invalid circuit error (as expected):"
            pr "  %s" ex.Message
            Ok ex.Message

    pr ""

    let note =
        match validationResult with
        | Ok msg -> sprintf "Caught: %s" (msg.Substring(0, min 60 msg.Length))
        | Error msg -> msg

    record
        { Name = "validate"
          Label = "Circuit Validation"
          Qubits = 2
          Gates = 0
          Note = note
          Qasm = None }

// --- JSON output ---

outputPath
|> Option.iter (fun path ->
    let payload =
        jsonResults
        |> List.map (fun r ->
            dict [
                "name", box r.Name
                "label", box r.Label
                "qubits", box r.Qubits
                "gates", box r.Gates
                "note", box r.Note
                yield! match r.Qasm with Some q -> [ "qasm", box q ] | None -> [] ])
    Reporting.writeJson path payload)

// --- CSV output ---

csvPath
|> Option.iter (fun path ->
    let header = [ "name"; "label"; "qubits"; "gates"; "note" ]
    Reporting.writeCsv path header csvRows)

// --- Summary ---

if not quiet then
    pr ""
    pr "=== Summary ==="
    pr "  Ran %d example(s)" (List.length jsonResults)
    jsonResults
    |> List.iter (fun r ->
        pr "  [OK] %-25s %d qubits, %d gates" r.Label r.Qubits r.Gates)
    pr ""

if not quiet && outputPath.IsNone && csvPath.IsNone && (argv |> Array.isEmpty) then
    pr "Tip: Use --output results.json or --csv results.csv to export data."
    pr "     Use --example bell to run a single example."
    pr "     Run with --help for all options."
