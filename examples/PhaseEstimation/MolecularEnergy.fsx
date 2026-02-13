#!/usr/bin/env dotnet fsi
// ============================================================================
// Quantum Phase Estimation - Molecular Energy Calculation
// ============================================================================
//
// Demonstrates eigenvalue extraction for quantum chemistry simulations.
// QPE extracts eigenvalues exponentially faster than classical methods,
// critical for drug discovery, materials science, and computational chemistry.
//
// Scenarios: T-gate (educational), molecular rotation, crystal lattice dynamics.
// Extensible starting point for QPE-based quantum chemistry workflows.
//
// ============================================================================

#r "../../src/FSharp.Azure.Quantum/bin/Debug/net10.0/FSharp.Azure.Quantum.dll"
#load "../_common/Cli.fs"
#load "../_common/Data.fs"
#load "../_common/Reporting.fs"

open System
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.QuantumPhaseEstimator
open FSharp.Azure.Quantum.Algorithms.QPE
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Backends.LocalBackend
open FSharp.Azure.Quantum.Examples.Common

// --- Quantum Backend (Rule 1) ---
let quantumBackend = LocalBackend() :> IQuantumBackend

// --- CLI ---
let argv = fsi.CommandLineArgs |> Array.skip 1
let args = Cli.parse argv

Cli.exitIfHelp
    "MolecularEnergy.fsx"
    "Quantum Phase Estimation for molecular energy calculation"
    [ { Name = "scenario"; Description = "Which scenario (all|tgate|molecular|crystal)"; Default = Some "all" }
      { Name = "precision"; Description = "Precision qubits for estimation"; Default = Some "10" }
      { Name = "theta"; Description = "Rotation angle for molecular scenario (radians)"; Default = Some "1.0472" }
      { Name = "phase-angle"; Description = "Phase angle for crystal scenario (radians)"; Default = Some "0.7854" }
      { Name = "output"; Description = "Write results to JSON file"; Default = None }
      { Name = "csv"; Description = "Write results to CSV file"; Default = None }
      { Name = "quiet"; Description = "Suppress console output"; Default = None } ]
    args

let quiet = Cli.hasFlag "quiet" args
let outputPath = Cli.tryGet "output" args
let csvPath = Cli.tryGet "csv" args
let scenario = Cli.getOr "scenario" "all" args
let cliPrecision = Cli.getIntOr "precision" 10 args
let theta = Cli.getFloatOr "theta" (Math.PI / 3.0) args
let phaseAngle = Cli.getFloatOr "phase-angle" (Math.PI / 4.0) args

let pr fmt = Printf.ksprintf (fun s -> if not quiet then printfn "%s" s) fmt
let shouldRun key = scenario = "all" || scenario = key

// --- Result Type ---

type QPEResult =
    { Scenario: string
      Label: string
      Phase: float
      ExpectedPhase: float
      PhaseError: float
      Qubits: int
      GateCount: int
      PrecisionBits: int
      Note: string }

let mutable jsonResults : QPEResult list = []
let mutable csvRows : string list list = []

let record (r: QPEResult) =
    jsonResults <- jsonResults @ [ r ]
    csvRows <- csvRows @ [
        [ r.Scenario; r.Label; sprintf "%.6f" r.Phase; sprintf "%.6f" r.ExpectedPhase
          sprintf "%.6f" r.PhaseError; string r.Qubits; string r.GateCount
          string r.PrecisionBits; r.Note ] ]

// ============================================================================
// SCENARIO 1: T-Gate Phase Estimation (Educational)
// ============================================================================

if shouldRun "tgate" then
    pr "--- Scenario 1: T-Gate Phase Estimation (Educational) ---"
    pr ""
    pr "The T-gate has eigenvalue e^(i*pi/4), so phase = 1/8 = 0.125"
    pr ""

    let tGateProblem = phaseEstimator {
        unitary TGate
        precision cliPrecision
        backend quantumBackend
    }

    match tGateProblem with
    | Ok prob ->
        match estimate prob with
        | Ok result ->
            let expected = 0.125
            let err = abs (result.Phase - expected)

            pr "  [OK] Phase Estimated!"
            pr "  Estimated Phase:  %.6f" result.Phase
            pr "  Expected Phase:   %.6f" expected
            pr "  Error:            %.6f" err
            pr "  Eigenvalue:       %.4f + %.4fi" result.Eigenvalue.Real result.Eigenvalue.Imaginary
            pr "  Qubits: %d  |  Gates: %d  |  Precision: %d bits" result.TotalQubits result.GateCount result.Precision
            pr ""

            record
                { Scenario = "tgate"; Label = "T-Gate Phase"
                  Phase = result.Phase; ExpectedPhase = expected; PhaseError = err
                  Qubits = result.TotalQubits; GateCount = result.GateCount
                  PrecisionBits = result.Precision
                  Note = sprintf "eigenvalue magnitude=%.4f" result.Eigenvalue.Magnitude }

        | Error err -> pr "  [ERROR] Execution: %s" err.Message

    | Error err -> pr "  [ERROR] Builder: %s" err.Message

// ============================================================================
// SCENARIO 2: Molecular Rotation Hamiltonian (Drug Design)
// ============================================================================

if shouldRun "molecular" then
    pr "--- Scenario 2: Molecular Rotation Hamiltonian ---"
    pr ""
    pr "  Modeling simplified Hamiltonian H = Rz(theta)"
    pr "  Rotation angle: %.4f radians (%.1f deg)" theta (theta * 180.0 / Math.PI)
    pr ""

    let molecularProblem = phaseEstimator {
        unitary (RotationZ theta)
        precision (max cliPrecision 12)
        targetQubits 1
        backend quantumBackend
    }

    match molecularProblem with
    | Ok prob ->
        match estimate prob with
        | Ok result ->
            let expectedPhase = theta / (2.0 * Math.PI)
            let err = abs (result.Phase - expectedPhase)
            let energyAU = result.Phase * 2.0 * Math.PI

            pr "  [OK] Molecular Energy Extracted!"
            pr "  Estimated Phase:  %.6f" result.Phase
            pr "  Expected Phase:   %.6f" expectedPhase
            pr "  Ground State E:   %.6f a.u." energyAU
            pr "  Phase Error:      %.6f" err
            pr "  Qubits: %d  |  Gates: %d  |  Precision: %d bits" result.TotalQubits result.GateCount prob.Precision
            pr ""
            pr "  Application: Lower energy = more stable molecular config"
            pr "  Predicts drug-protein binding affinity"
            pr ""

            record
                { Scenario = "molecular"; Label = "Molecular Rotation"
                  Phase = result.Phase; ExpectedPhase = expectedPhase; PhaseError = err
                  Qubits = result.TotalQubits; GateCount = result.GateCount
                  PrecisionBits = prob.Precision
                  Note = sprintf "energy=%.6f a.u., theta=%.4f" energyAU theta }

        | Error err -> pr "  [ERROR] Execution: %s" err.Message

    | Error err -> pr "  [ERROR] Builder: %s" err.Message

// ============================================================================
// SCENARIO 3: Crystal Lattice Dynamics (Materials Science)
// ============================================================================

if shouldRun "crystal" then
    pr "--- Scenario 3: Crystal Lattice Dynamics ---"
    pr ""
    pr "  Phase angle: %.4f radians (%.1f deg)" phaseAngle (phaseAngle * 180.0 / Math.PI)
    pr "  Application: Electronic band structure prediction"
    pr ""

    let materialProblem = phaseEstimator {
        unitary (PhaseGate phaseAngle)
        precision (max cliPrecision 12)
        backend quantumBackend
    }

    match materialProblem with
    | Ok problem ->
        match estimate problem with
        | Ok result ->
            let expectedPhase = phaseAngle / (2.0 * Math.PI)
            let err = abs (result.Phase - expectedPhase)

            pr "  [OK] Band Structure Eigenvalue Extracted!"
            pr "  Bloch Phase:      %.6f" result.Phase
            pr "  Expected Phase:   %.6f" expectedPhase
            pr "  Measurement Error: %.6f" err
            pr "  Qubits: %d  |  Gates: %d" result.TotalQubits result.GateCount
            pr ""
            pr "  Applications: semiconductor design, solar cells, superconductors"
            pr ""

            record
                { Scenario = "crystal"; Label = "Crystal Lattice"
                  Phase = result.Phase; ExpectedPhase = expectedPhase; PhaseError = err
                  Qubits = result.TotalQubits; GateCount = result.GateCount
                  PrecisionBits = problem.Precision
                  Note = sprintf "phaseAngle=%.4f rad" phaseAngle }

        | Error err -> pr "  [ERROR] %s" err.Message

    | Error err -> pr "  [ERROR] Builder: %s" err.Message

// --- JSON output ---

outputPath
|> Option.iter (fun path ->
    let payload =
        jsonResults
        |> List.map (fun r ->
            dict [
                "scenario", box r.Scenario
                "label", box r.Label
                "phase", box r.Phase
                "expectedPhase", box r.ExpectedPhase
                "phaseError", box r.PhaseError
                "qubits", box r.Qubits
                "gateCount", box r.GateCount
                "precisionBits", box r.PrecisionBits
                "note", box r.Note ])
    Reporting.writeJson path payload)

// --- CSV output ---

csvPath
|> Option.iter (fun path ->
    let header = [ "scenario"; "label"; "phase"; "expectedPhase"; "phaseError"; "qubits"; "gateCount"; "precisionBits"; "note" ]
    Reporting.writeCsv path header csvRows)

// --- Summary ---

if not quiet then
    pr ""
    pr "=== Summary ==="
    jsonResults
    |> List.iter (fun r ->
        pr "  [OK] %-25s phase=%.6f (err=%.6f) %d qubits" r.Label r.Phase r.PhaseError r.Qubits)
    pr ""
    pr "Key: QPE extracts eigenvalues in O(poly(n)/eps) vs O(N^3) classical"
    pr ""

if not quiet && outputPath.IsNone && csvPath.IsNone && (argv |> Array.isEmpty) then
    pr "Tip: Use --output results.json or --csv results.csv to export data."
    pr "     Use --scenario tgate to run a single scenario."
    pr "     Use --precision 14 for higher accuracy."
    pr "     Run with --help for all options."
