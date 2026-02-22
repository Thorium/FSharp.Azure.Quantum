#!/usr/bin/env dotnet fsi
// ============================================================================
// Quantum Circuit Visualization - Easy Integration Example
// ============================================================================
//
// Demonstrates how visualization is easily integrated with CircuitBuilder
// using simple extension methods (.ToASCII(), .ToMermaid()).
//
// Examples: Bell state, QFT-3, rotation gates with controlled operations.
// Extensible starting point for circuit visualization workflows.
//
// ============================================================================

#r "nuget: Microsoft.Extensions.Logging.Abstractions, 10.0.0"
#r "../../src/FSharp.Azure.Quantum/bin/Debug/net10.0/FSharp.Azure.Quantum.dll"
#load "../_common/Cli.fs"
#load "../_common/Data.fs"
#load "../_common/Reporting.fs"

open System
open FSharp.Azure.Quantum.CircuitBuilder
open FSharp.Azure.Quantum.Visualization
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Backends.LocalBackend
open FSharp.Azure.Quantum.Examples.Common

// IQuantumBackend available for downstream circuit execution
let quantumBackend = LocalBackend() :> IQuantumBackend

// --- CLI ---
let argv = fsi.CommandLineArgs |> Array.skip 1
let args = Cli.parse argv

Cli.exitIfHelp
    "CircuitVisualization.fsx"
    "Quantum circuit visualization with ASCII and Mermaid output"
    [ { Name = "example"; Description = "Which example (all|bell|qft|rotations)"; Default = Some "all" }
      { Name = "format"; Description = "Output format (all|ascii|mermaid)"; Default = Some "all" }
      { Name = "mermaid-file"; Description = "Write Mermaid diagram to file"; Default = None }
      { Name = "output"; Description = "Write results to JSON file"; Default = None }
      { Name = "csv"; Description = "Write results to CSV file"; Default = None }
      { Name = "quiet"; Description = "Suppress console output"; Default = None } ]
    args

let quiet = Cli.hasFlag "quiet" args
let outputPath = Cli.tryGet "output" args
let csvPath = Cli.tryGet "csv" args
let mermaidFile = Cli.tryGet "mermaid-file" args
let example = Cli.getOr "example" "all" args
let format = Cli.getOr "format" "all" args

let pr fmt = Printf.ksprintf (fun s -> if not quiet then printfn "%s" s) fmt

// --- Circuit Definitions ---

let bellCircuit = circuit {
    qubits 2
    H 0
    CNOT (0, 1)
}

let qft3Circuit = circuit {
    qubits 3
    H 0
    CP (1, 0, Math.PI / 2.0)
    CP (2, 0, Math.PI / 4.0)
    H 1
    CP (2, 1, Math.PI / 2.0)
    H 2
    SWAP (0, 2)
}

let rotationsCircuit = circuit {
    qubits 3
    RX (0, Math.PI / 4.0)
    RY (1, Math.PI / 3.0)
    RZ (2, Math.PI / 2.0)
    CRX (0, 1, Math.PI / 6.0)
    CRY (1, 2, Math.PI / 8.0)
    Measure 0
    Measure 1
    Measure 2
}

// --- Example Runner ---

type CircuitInfo =
    { Name: string
      Label: string
      Circuit: Circuit
      Qubits: int
      Gates: int
      AsciiDiagram: string
      MermaidDiagram: string }

let buildInfo (name: string) (label: string) (c: Circuit) =
    { Name = name
      Label = label
      Circuit = c
      Qubits = c.QubitCount
      Gates = List.length c.Gates
      AsciiDiagram = c.ToASCII()
      MermaidDiagram = c.ToMermaid() }

let allExamples =
    [ ("bell", "Bell State (Entanglement)", bellCircuit)
      ("qft", "QFT-3 (Quantum Fourier Transform)", qft3Circuit)
      ("rotations", "Rotation Gates + Controlled Ops", rotationsCircuit) ]

let selected =
    if example = "all" then allExamples
    else allExamples |> List.filter (fun (key, _, _) -> key = example)

let results =
    selected
    |> List.map (fun (key, label, c) ->
        let info = buildInfo key label c

        pr "=== %s ===" info.Label
        pr "  Qubits: %d  |  Gates: %d" info.Qubits info.Gates
        pr ""

        if format = "all" || format = "ascii" then
            pr "ASCII Visualization:"
            pr "--------------------"
            pr "%s" info.AsciiDiagram
            pr ""

        if format = "all" || format = "mermaid" then
            pr "Mermaid Sequence Diagram:"
            pr "-------------------------"
            pr "%s" info.MermaidDiagram
            pr ""

        info)

// --- Mermaid file export ---

mermaidFile
|> Option.iter (fun path ->
    let content =
        results
        |> List.map (fun r -> sprintf "%% %s\n%s" r.Label r.MermaidDiagram)
        |> String.concat "\n\n"
    IO.File.WriteAllText(path, content)
    pr "Mermaid diagrams written to %s" path)

// --- JSON output ---

outputPath
|> Option.iter (fun path ->
    let payload =
        results
        |> List.map (fun r ->
            dict [
                "name", box r.Name
                "label", box r.Label
                "qubits", box r.Qubits
                "gates", box r.Gates
                "ascii", box r.AsciiDiagram
                "mermaid", box r.MermaidDiagram ])
    Reporting.writeJson path payload)

// --- CSV output ---

csvPath
|> Option.iter (fun path ->
    let header = [ "name"; "label"; "qubits"; "gates" ]
    let rows =
        results
        |> List.map (fun r ->
            [ r.Name; r.Label; string r.Qubits; string r.Gates ])
    Reporting.writeCsv path header rows)

// --- Usage hint ---

if not quiet && outputPath.IsNone && csvPath.IsNone && (argv |> Array.isEmpty) then
    pr ""
    pr "Tip: Use --output results.json or --csv results.csv to export data."
    pr "     Use --format ascii or --format mermaid to filter output."
    pr "     Use --mermaid-file diagrams.md to export Mermaid diagrams."
    pr "     Run with --help for all options."
