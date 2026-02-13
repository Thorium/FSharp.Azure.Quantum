/// Quantum Machine Learning - Variational Form (Ansatz) Example
///
/// Demonstrates different variational form architectures for parameterized
/// quantum circuits. The ansatz determines the expressiveness and trainability
/// of a VQC model.
///
/// Examples:
///   1. RealAmplitudes (depth 1) — Ry + CZ
///   2. RealAmplitudes (depth 2) — deeper expressiveness
///   3. TwoLocal (Ry+CZ) — flexible rotation + entanglement
///   4. TwoLocal (Rx+CNOT) — different gate choice
///   5. EfficientSU2 — full SU(2) coverage (2x parameters)
///   6. Ansatz comparison table
///   7. Parameter initialization strategies
///   8. Feature map + ansatz composition
///   9. Depth scaling analysis
///
/// Run from repo root:
///   dotnet fsi examples/QML/VariationalFormExample.fsx
///   dotnet fsi examples/QML/VariationalFormExample.fsx -- --example 6
///   dotnet fsi examples/QML/VariationalFormExample.fsx -- --qubits 6 --depth 3

(*
===============================================================================
 Background Theory
===============================================================================

A variational form (or ansatz) is the parameterized quantum circuit V(theta) that
transforms feature-encoded states into predictions. This is directly analogous to
choosing a neural network architecture in classical deep learning.

Expressibility vs. Trainability Tradeoff:
  Highly expressive ansatze can approximate any unitary but suffer from barren
  plateaus (vanishing gradients). For NISQ devices, 1-3 layers balance
  expressibility with trainability.

Key Ansatz Architectures:
  - RealAmplitudes: Ry + CZ. Hardware-efficient, real amplitudes only.
  - TwoLocal: Configurable rotation + entanglement. More flexible.
  - EfficientSU2: Ry + Rz (full SU(2)) + CZ. 2x parameters but complex amplitudes.

References:
  [1] McClean et al., Nature Comm. 9, 4812 (2018) — barren plateaus.
  [2] Sim et al., Adv. Quantum Technol. 2, 1900070 (2019) — expressibility.
*)

//#r "nuget: FSharp.Azure.Quantum"
#r "../../src/FSharp.Azure.Quantum/bin/Debug/net10.0/FSharp.Azure.Quantum.dll"

#load "../_common/Cli.fs"
#load "../_common/Data.fs"
#load "../_common/Reporting.fs"

open System
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.MachineLearning
open FSharp.Azure.Quantum.MachineLearning.VariationalForms
open FSharp.Azure.Quantum.CircuitBuilder
open FSharp.Azure.Quantum.Backends.LocalBackend
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Examples.Common

// ── CLI ──────────────────────────────────────────────────────────────
let argv = fsi.CommandLineArgs |> Array.skip 1
let args = Cli.parse argv

Cli.exitIfHelp
    "VariationalFormExample.fsx"
    "Variational form (ansatz) architectures for QML"
    [ { Name = "example"; Description = "Which example: 1-9|all";     Default = Some "all" }
      { Name = "qubits";  Description = "Number of qubits";           Default = Some "4" }
      { Name = "depth";   Description = "Ansatz depth (layers)";      Default = Some "1" }
      { Name = "output";  Description = "Write results to JSON file"; Default = None }
      { Name = "csv";     Description = "Write results to CSV file";  Default = None }
      { Name = "quiet";   Description = "Suppress console output";    Default = None } ]
    args

let quiet      = Cli.hasFlag "quiet" args
let outputPath = Cli.tryGet "output" args
let csvPath    = Cli.tryGet "csv" args
let exampleArg = Cli.getOr "example" "all" args
let numQubits  = Cli.getIntOr "qubits" 4 args
let cliDepth   = Cli.getIntOr "depth" 1 args

let pr fmt = Printf.ksprintf (fun s -> if not quiet then printfn "%s" s) fmt

let section title =
    pr ""
    pr "%s" (String.replicate 60 "-")
    pr "%s" title
    pr "%s" (String.replicate 60 "-")

// ── Quantum Backend (Rule 1) ────────────────────────────────────────
let quantumBackend = LocalBackend() :> IQuantumBackend

// ── Result accumulators ─────────────────────────────────────────────
let mutable results : Map<string, obj> list = []
let mutable csvRows : string list list = []

let shouldRun ex = exampleArg = "all" || exampleArg = string ex

let analyzeCircuit name (circ: Circuit) nParams =
    let gates = getGates circ
    let ryCount  = gates |> List.filter (function RY _ -> true | _ -> false) |> List.length
    let rzCount  = gates |> List.filter (function RZ _ -> true | _ -> false) |> List.length
    let rxCount  = gates |> List.filter (function RX _ -> true | _ -> false) |> List.length
    let czCount  = gates |> List.filter (function CZ _ -> true | _ -> false) |> List.length
    let cnotCount = gates |> List.filter (function CNOT _ -> true | _ -> false) |> List.length
    let rotCount = ryCount + rzCount + rxCount
    let entCount = czCount + cnotCount
    (rotCount, entCount, ryCount, rzCount, rxCount, czCount, cnotCount)

let addRow name nParams totalGates rotGates entGates =
    results <- results @ [
        Map.ofList [
            "example", box name
            "qubits", box numQubits
            "parameters", box nParams
            "total_gates", box totalGates
            "rotation_gates", box rotGates
            "entangling_gates", box entGates
        ]
    ]
    csvRows <- csvRows @ [
        [ name; string numQubits; string nParams; string totalGates
          string rotGates; string entGates ]
    ]

// ── EXAMPLE 1: RealAmplitudes (depth from CLI) ──────────────────────
if shouldRun 1 then
    section (sprintf "EXAMPLE 1: RealAmplitudes (depth=%d)" cliDepth)
    pr "Strategy: Ry rotations + CZ entanglement"
    pr ""

    let vParams = randomParameters (RealAmplitudes cliDepth) numQubits (Some 42)
    pr "Parameters needed: %d" vParams.Length

    match buildVariationalForm (RealAmplitudes cliDepth) vParams numQubits with
    | Ok circ ->
        let (rot, ent, ry, _, _, cz, _) = analyzeCircuit "1_real_amp" circ vParams.Length
        pr "Circuit: %d qubits, %d gates" circ.QubitCount (gateCount circ)
        pr "  Ry: %d, CZ: %d" ry cz
        addRow "1_real_amplitudes" vParams.Length (gateCount circ) rot ent
    | Error err ->
        pr "Error: %s" err.Message

// ── EXAMPLE 2: RealAmplitudes (depth 2) ─────────────────────────────
if shouldRun 2 then
    section "EXAMPLE 2: RealAmplitudes (depth=2)"
    pr "Strategy: Two layers of Ry + CZ — more expressiveness"
    pr ""

    let vParams = randomParameters (RealAmplitudes 2) numQubits (Some 42)
    pr "Parameters needed: %d" vParams.Length

    match buildVariationalForm (RealAmplitudes 2) vParams numQubits with
    | Ok circ ->
        let (rot, ent, ry, _, _, cz, _) = analyzeCircuit "2_real_d2" circ vParams.Length
        pr "Circuit: %d qubits, %d gates" circ.QubitCount (gateCount circ)
        pr "  Ry: %d, CZ: %d" ry cz
        addRow "2_real_amp_d2" vParams.Length (gateCount circ) rot ent
    | Error err ->
        pr "Error: %s" err.Message

// ── EXAMPLE 3: TwoLocal (Ry + CZ) ──────────────────────────────────
if shouldRun 3 then
    section "EXAMPLE 3: TwoLocal (Ry + CZ, depth=1)"
    pr "Strategy: Flexible rotation + entanglement choice"
    pr ""

    let vParams = randomParameters (TwoLocal("Ry", "CZ", 1)) numQubits (Some 42)
    pr "Parameters needed: %d" vParams.Length

    match buildVariationalForm (TwoLocal("Ry", "CZ", 1)) vParams numQubits with
    | Ok circ ->
        let (rot, ent, ry, _, _, cz, _) = analyzeCircuit "3_twolocal_rycz" circ vParams.Length
        pr "Circuit: %d qubits, %d gates" circ.QubitCount (gateCount circ)
        pr "  Ry: %d, CZ: %d" ry cz
        addRow "3_twolocal_rycz" vParams.Length (gateCount circ) rot ent
    | Error err ->
        pr "Error: %s" err.Message

// ── EXAMPLE 4: TwoLocal (Rx + CNOT) ────────────────────────────────
if shouldRun 4 then
    section "EXAMPLE 4: TwoLocal (Rx + CNOT, depth=1)"
    pr "Strategy: Different rotation and entanglement gates"
    pr ""

    let vParams = randomParameters (TwoLocal("Rx", "CNOT", 1)) numQubits (Some 42)
    pr "Parameters needed: %d" vParams.Length

    match buildVariationalForm (TwoLocal("Rx", "CNOT", 1)) vParams numQubits with
    | Ok circ ->
        let (rot, ent, _, _, rx, _, cnot) = analyzeCircuit "4_twolocal_rxcnot" circ vParams.Length
        pr "Circuit: %d qubits, %d gates" circ.QubitCount (gateCount circ)
        pr "  Rx: %d, CNOT: %d" rx cnot
        addRow "4_twolocal_rxcnot" vParams.Length (gateCount circ) rot ent
    | Error err ->
        pr "Error: %s" err.Message

// ── EXAMPLE 5: EfficientSU2 ────────────────────────────────────────
if shouldRun 5 then
    section "EXAMPLE 5: EfficientSU2 (depth=1)"
    pr "Strategy: Ry + Rz rotations = full SU(2) coverage"
    pr "  Requires 2x parameters per qubit (Ry AND Rz)"
    pr ""

    let vParams = randomParameters (EfficientSU2 1) numQubits (Some 42)
    pr "Parameters needed: %d (2 x qubits x depth)" vParams.Length

    match buildVariationalForm (EfficientSU2 1) vParams numQubits with
    | Ok circ ->
        let (rot, ent, ry, rz, _, cz, _) = analyzeCircuit "5_su2" circ vParams.Length
        pr "Circuit: %d qubits, %d gates" circ.QubitCount (gateCount circ)
        pr "  Ry: %d, Rz: %d, CZ: %d" ry rz cz
        addRow "5_efficient_su2" vParams.Length (gateCount circ) rot ent
    | Error err ->
        pr "Error: %s" err.Message

// ── EXAMPLE 6: Ansatz Comparison ────────────────────────────────────
if shouldRun 6 then
    section "EXAMPLE 6: Ansatz Comparison (all architectures)"

    let ansatze = [
        ("RealAmplitudes",   RealAmplitudes 1)
        ("TwoLocal(Ry+CZ)",  TwoLocal("Ry", "CZ", 1))
        ("TwoLocal(Rx+CNOT)", TwoLocal("Rx", "CNOT", 1))
        ("EfficientSU2",     EfficientSU2 1)
    ]

    pr "%-22s | %6s | %5s | %8s | %8s" "Ansatz" "Params" "Gates" "Rotation" "Entangle"
    pr "%s" (String.replicate 60 "-")

    for (name, ansatz) in ansatze do
        let vParams = randomParameters ansatz numQubits (Some 42)
        match buildVariationalForm ansatz vParams numQubits with
        | Ok circ ->
            let (rot, ent, _, _, _, _, _) = analyzeCircuit name circ vParams.Length
            pr "%-22s | %6d | %5d | %8d | %8d"
                name vParams.Length (gateCount circ) rot ent
            addRow (sprintf "6_%s" (name.Replace("(","").Replace(")","")))
                vParams.Length (gateCount circ) rot ent
        | Error _ ->
            pr "%-22s | %6s | %5s | %8s | %8s" name "Err" "Err" "Err" "Err"

// ── EXAMPLE 7: Parameter Initialization ─────────────────────────────
if shouldRun 7 then
    section "EXAMPLE 7: Parameter Initialization Strategies"

    let ansatz = RealAmplitudes 2

    let zero = zeroParameters ansatz numQubits
    let cnst = constantParameters ansatz numQubits (Math.PI / 4.0)
    let rand = randomParameters ansatz numQubits (Some 42)

    pr "Ansatz: RealAmplitudes(depth=2), %d qubits" numQubits
    pr ""
    pr "1. Zero init:     %A" (zero |> Array.take (min 5 zero.Length))
    pr "2. Constant pi/4: %A" (cnst |> Array.take (min 5 cnst.Length) |> Array.map (fun p -> Math.Round(p, 3)))
    pr "3. Random (seed): %A" (rand |> Array.take (min 5 rand.Length) |> Array.map (fun p -> Math.Round(p, 3)))

    results <- results @ [
        Map.ofList [
            "example", box "7_init_strategies"
            "zero_params", box zero.Length
            "constant_value", box (Math.PI / 4.0)
            "random_seed", box 42
        ]
    ]

// ── EXAMPLE 8: Feature Map + Ansatz Composition ─────────────────────
if shouldRun 8 then
    section "EXAMPLE 8: Feature Map + Ansatz Composition"

    let feat = [| 0.5; 1.0; -0.3; 0.8 |]
    let fmType = ZZFeatureMap 1
    let vfType = RealAmplitudes 1
    let vParams = randomParameters vfType numQubits (Some 42)

    pr "Feature map: ZZFeatureMap(1)"
    pr "Ansatz:      RealAmplitudes(1)"
    pr ""

    match FeatureMap.buildFeatureMap fmType feat with
    | Ok fmCircuit ->
        pr "1. Feature map:  %d gates" (gateCount fmCircuit)
        match buildVariationalForm vfType vParams numQubits with
        | Ok vfCircuit ->
            pr "2. Ansatz:       %d gates" (gateCount vfCircuit)
            match composeWithFeatureMap fmCircuit vfCircuit with
            | Ok composed ->
                pr "3. Composed:     %d gates (= %d + %d)"
                    (gateCount composed) (gateCount fmCircuit) (gateCount vfCircuit)
                pr ""
                pr "This is the complete VQC forward pass circuit!"

                results <- results @ [
                    Map.ofList [
                        "example", box "8_composition"
                        "feature_map_gates", box (gateCount fmCircuit)
                        "ansatz_gates", box (gateCount vfCircuit)
                        "composed_gates", box (gateCount composed)
                    ]
                ]
            | Error err -> pr "Composition error: %s" err.Message
        | Error err -> pr "Ansatz error: %s" err.Message
    | Error err -> pr "Feature map error: %s" err.Message

// ── EXAMPLE 9: Depth Scaling ────────────────────────────────────────
if shouldRun 9 then
    section "EXAMPLE 9: Depth Scaling (RealAmplitudes)"

    pr "%-5s | %6s | %5s | %4s | %4s" "Depth" "Params" "Gates" "Ry" "CZ"
    pr "%s" (String.replicate 35 "-")

    for d in [1; 2; 3; 5] do
        let ansatz = RealAmplitudes d
        let vParams = randomParameters ansatz numQubits (Some 42)
        match buildVariationalForm ansatz vParams numQubits with
        | Ok circ ->
            let gates = getGates circ
            let ry = gates |> List.filter (function RY _ -> true | _ -> false) |> List.length
            let cz = gates |> List.filter (function CZ _ -> true | _ -> false) |> List.length
            pr "%5d | %6d | %5d | %4d | %4d" d vParams.Length (gateCount circ) ry cz
            addRow (sprintf "9_depth_%d" d) vParams.Length (gateCount circ) ry cz
        | Error _ ->
            pr "%5d | %6s | %5s | %4s | %4s" d "Err" "Err" "--" "--"

// ── Output ───────────────────────────────────────────────────────────
let payload =
    Map.ofList [
        "script", box "VariationalFormExample.fsx"
        "timestamp", box (DateTime.UtcNow.ToString("o"))
        "qubits", box numQubits
        "depth", box cliDepth
        "example", box exampleArg
        "backend", box (quantumBackend.Name)
        "results", box results
    ]

outputPath |> Option.iter (fun p -> Reporting.writeJson p payload)
csvPath    |> Option.iter (fun p ->
    Reporting.writeCsv p
        [ "example"; "qubits"; "parameters"; "total_gates"; "rotation_gates"; "entangling_gates" ]
        csvRows)

// ── Usage hints ──────────────────────────────────────────────────────
if not quiet && outputPath.IsNone && csvPath.IsNone && argv.Length = 0 then
    pr ""
    pr "Usage hints:"
    pr "  dotnet fsi VariationalFormExample.fsx -- --example 6"
    pr "  dotnet fsi VariationalFormExample.fsx -- --qubits 6 --depth 3"
    pr "  dotnet fsi VariationalFormExample.fsx -- --quiet --output r.json --csv r.csv"
    pr "  dotnet fsi VariationalFormExample.fsx -- --help"
