/// Quantum Machine Learning - Feature Map Example
///
/// Demonstrates different feature map strategies for encoding classical data
/// into quantum states. Feature maps are the bridge between classical and
/// quantum computation â€” they determine how your data is represented in
/// quantum Hilbert space.
///
/// Examples:
///   1. Angle encoding (Ry rotations)
///   2. ZZ feature map depth 1 (entangling)
///   3. ZZ feature map depth 2 (deeper entanglement)
///   4. Pauli feature map (custom Pauli strings)
///   5. Amplitude encoding (exponential compression)
///   6. Comparison table of all feature maps
///   7. Scaling analysis (2, 4, 8 features)
///
/// Run from repo root:
///   dotnet fsi examples/QML/FeatureMapExample.fsx
///   dotnet fsi examples/QML/FeatureMapExample.fsx -- --example 2
///   dotnet fsi examples/QML/FeatureMapExample.fsx -- --features "0.3,0.7,0.1,0.9"
///   dotnet fsi examples/QML/FeatureMapExample.fsx -- --quiet --output r.json

(*
===============================================================================
 Background Theory
===============================================================================

Quantum feature maps are the bridge between classical data and quantum computation.
They encode classical input vectors x âˆˆ â„áµˆ into quantum states |Ïˆ(x)âŸ© in a 2â¿-
dimensional Hilbert space. The choice of feature map critically determines the
expressiveness and potential quantum advantage of variational quantum algorithms.
Different encoding strategies create different "quantum feature spaces" that may
capture patterns invisible to classical methods.

The most common encoding strategies are:
- **Angle Encoding**: Each feature xáµ¢ is encoded as a rotation angle, typically
  via Ry(Ï€Â·xáµ¢) or Rz(xáµ¢) gates. Requires n qubits for n features. Simple and
  hardware-efficient but limited expressiveness.
- **Amplitude Encoding**: Features are encoded as amplitudes of a quantum state,
  |ÏˆâŸ© = Î£áµ¢ xáµ¢|iâŸ© (normalized). Exponentially efficient (logâ‚‚(d) qubits for d
  features) but requires complex state preparation circuits.
- **ZZ Feature Map**: Combines single-qubit rotations with two-qubit ZZ interactions
  that encode products of features: exp(iÂ·Ï†(xáµ¢,xâ±¼)Â·Záµ¢Zâ±¼). Creates entanglement
  and captures feature correlations, proven advantageous in Havlicek et al.

Key Equations:
  - Angle encoding: |Ïˆ(x)âŸ© = âŠ—áµ¢ Ry(Ï€Â·xáµ¢)|0âŸ© = âŠ—áµ¢ [cos(Ï€xáµ¢/2)|0âŸ© + sin(Ï€xáµ¢/2)|1âŸ©]
  - ZZ feature map layer: U_ZZ = exp(iÂ·(Ï€-xáµ¢)(Ï€-xâ±¼)Â·Záµ¢Zâ±¼) for connected qubits
  - Quantum kernel: K(x,x') = |âŸ¨Ïˆ(x)|Ïˆ(x')âŸ©|Â² (overlap of encoded states)
  - Expressibility: measured by distribution of fidelities over parameter space

References:
  [1] Havlicek et al., Nature 567, 209-212 (2019).
  [2] Schuld & Killoran, Phys. Rev. Lett. 122, 040504 (2019).
  [3] LaRose & Coyle, Phys. Rev. A 102, 032420 (2020).
*)

//#r "nuget: FSharp.Azure.Quantum"
#r "nuget: Microsoft.Extensions.Logging.Abstractions, 10.0.0"
#r "../../src/FSharp.Azure.Quantum/bin/Debug/net10.0/FSharp.Azure.Quantum.dll"

#load "../_common/Cli.fs"
#load "../_common/Data.fs"
#load "../_common/Reporting.fs"

open System
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.MachineLearning
open FSharp.Azure.Quantum.CircuitBuilder
open FSharp.Azure.Quantum.Backends.LocalBackend
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Examples.Common

// â”€â”€ CLI â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
let argv = fsi.CommandLineArgs |> Array.skip 1
let args = Cli.parse argv

Cli.exitIfHelp
    "FeatureMapExample.fsx"
    "Quantum feature map encoding strategies for QML"
    [ { Name = "example";  Description = "Which example: 1|2|3|4|5|6|7|all"; Default = Some "all" }
      { Name = "features"; Description = "Comma-separated feature vector";    Default = Some "0.5,1.0,-0.3,0.8" }
      { Name = "output";   Description = "Write results to JSON file";        Default = None }
      { Name = "csv";      Description = "Write results to CSV file";         Default = None }
      { Name = "quiet";    Description = "Suppress console output";           Default = None } ]
    args

let quiet      = Cli.hasFlag "quiet" args
let outputPath = Cli.tryGet "output" args
let csvPath    = Cli.tryGet "csv" args
let exampleArg = Cli.getOr "example" "all" args
let features   =
    Cli.getOr "features" "0.5,1.0,-0.3,0.8" args
    |> fun s -> s.Split(',') |> Array.map float

let pr fmt = Printf.ksprintf (fun s -> if not quiet then printfn "%s" s) fmt

let section title =
    pr ""
    pr "%s" (String.replicate 60 "-")
    pr "%s" title
    pr "%s" (String.replicate 60 "-")

// â”€â”€ Quantum Backend (Rule 1) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
// Feature maps generate circuits for quantum backends.
// The backend is available for downstream execution (see VQCExample).
let quantumBackend = LocalBackend() :> IQuantumBackend

// â”€â”€ Result accumulators â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
let mutable results : Map<string, obj> list = []
let mutable csvRows : string list list = []

let shouldRun ex = exampleArg = "all" || exampleArg = string ex

let analyzeGates (gates: Gate list) =
    let hCount    = gates |> List.filter (function H _ -> true | _ -> false) |> List.length
    let rzCount   = gates |> List.filter (function RZ _ -> true | _ -> false) |> List.length
    let ryCount   = gates |> List.filter (function RY _ -> true | _ -> false) |> List.length
    let cnotCount = gates |> List.filter (function CNOT _ -> true | _ -> false) |> List.length
    let czCount   = gates |> List.filter (function CZ _ -> true | _ -> false) |> List.length
    let swapCount = gates |> List.filter (function SWAP _ -> true | _ -> false) |> List.length
    (hCount, rzCount, ryCount, cnotCount, czCount, swapCount)

let hasEntanglement (gates: Gate list) =
    gates |> List.exists (function CNOT _ | CZ _ | SWAP _ | CCX _ -> true | _ -> false)

let addResult name qubits gateCount entangled extras =
    results <- results @ [
        Map.ofList ([
            "example", box name
            "qubits", box qubits
            "gates", box gateCount
            "entangled", box entangled
        ] @ extras)
    ]
    csvRows <- csvRows @ [
        [ name; string qubits; string gateCount; string entangled ]
    ]

// â”€â”€ EXAMPLE 1: Angle Encoding â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
if shouldRun 1 then
    section "EXAMPLE 1: Angle Encoding"
    pr "Strategy: Ry(pi * x_i) on each qubit â€” one qubit per feature"
    pr ""

    match FeatureMap.buildFeatureMap AngleEncoding features with
    | Ok circ ->
        pr "Circuit generated: %d qubits, %d gates" circ.QubitCount (gateCount circ)
        pr "  Structure: Ry rotations only (no entanglement)"
        addResult "1_angle" circ.QubitCount (gateCount circ) false []
    | Error err ->
        pr "Error: %s" err.Message

// â”€â”€ EXAMPLE 2: ZZ Feature Map (depth 1) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
if shouldRun 2 then
    section "EXAMPLE 2: ZZ Feature Map (depth=1)"
    pr "Strategy: Hadamard + Rz rotations + ZZ entanglement"
    pr ""

    match FeatureMap.buildFeatureMap (ZZFeatureMap 1) features with
    | Ok circ ->
        let gates = getGates circ
        let (h, rz, _, cnot, _, _) = analyzeGates gates
        pr "Circuit generated: %d qubits, %d gates" circ.QubitCount (gateCount circ)
        pr "  Hadamard: %d, Rz: %d, CNOT: %d" h rz cnot
        addResult "2_zz_d1" circ.QubitCount (gateCount circ) true
            [ "hadamard", box h; "rz", box rz; "cnot", box cnot ]
    | Error err ->
        pr "Error: %s" err.Message

// â”€â”€ EXAMPLE 3: ZZ Feature Map (depth 2) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
if shouldRun 3 then
    section "EXAMPLE 3: ZZ Feature Map (depth=2)"
    pr "Strategy: Two layers of entangling feature maps"
    pr ""

    match FeatureMap.buildFeatureMap (ZZFeatureMap 2) features with
    | Ok circ ->
        pr "Circuit generated: %d qubits, %d gates" circ.QubitCount (gateCount circ)
        pr "  Depth 2 = more expressive, captures deeper correlations"
        addResult "3_zz_d2" circ.QubitCount (gateCount circ) true []
    | Error err ->
        pr "Error: %s" err.Message

// â”€â”€ EXAMPLE 4: Pauli Feature Map â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
if shouldRun 4 then
    section "EXAMPLE 4: Pauli Feature Map"
    pr "Strategy: Custom Pauli string rotations (ZZ, XX)"
    pr ""

    match FeatureMap.buildFeatureMap (PauliFeatureMap(["ZZ"; "XX"], 1)) features with
    | Ok circ ->
        pr "Circuit generated: %d qubits, %d gates" circ.QubitCount (gateCount circ)
        pr "  Pauli strings: ZZ, XX"
        addResult "4_pauli" circ.QubitCount (gateCount circ) true []
    | Error err ->
        pr "Error: %s" err.Message

// â”€â”€ EXAMPLE 5: Amplitude Encoding â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
if shouldRun 5 then
    section "EXAMPLE 5: Amplitude Encoding"
    pr "Strategy: Encode features as quantum state amplitudes"
    pr "  Uses log2(d) qubits for d features â€” exponential compression"
    pr ""

    match FeatureMap.buildFeatureMap AmplitudeEncoding features with
    | Ok circ ->
        pr "Circuit generated: %d qubits (log2(%d) = %.1f), %d gates"
            circ.QubitCount features.Length
            (log (float features.Length) / log 2.0)
            (gateCount circ)
        addResult "5_amplitude" circ.QubitCount (gateCount circ) false []
    | Error err ->
        pr "Error: %s" err.Message

// â”€â”€ EXAMPLE 6: Feature Map Comparison â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
if shouldRun 6 then
    section "EXAMPLE 6: Feature Map Comparison"

    let featureMaps = [
        ("AngleEncoding",   AngleEncoding)
        ("ZZFeatureMap(1)", ZZFeatureMap 1)
        ("ZZFeatureMap(2)", ZZFeatureMap 2)
        ("PauliFeatureMap", PauliFeatureMap(["ZZ"], 1))
        ("AmplitudeEncoding", AmplitudeEncoding)
    ]

    pr "%-20s | %6s | %5s | %s" "Feature Map" "Qubits" "Gates" "Entanglement"
    pr "%s" (String.replicate 60 "-")

    for (name, fm) in featureMaps do
        match FeatureMap.buildFeatureMap fm features with
        | Ok circ ->
            let ent = hasEntanglement (getGates circ)
            let entStr = if ent then "Yes" else "No"
            pr "%-20s | %6d | %5d | %s" name circ.QubitCount (gateCount circ) entStr
            addResult (sprintf "6_%s" (name.Replace("(", "").Replace(")", "")))
                circ.QubitCount (gateCount circ) ent []
        | Error _ ->
            pr "%-20s | %6s | %5s | %s" name "Error" "Error" "Error"

// â”€â”€ EXAMPLE 7: Scaling Analysis â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
if shouldRun 7 then
    section "EXAMPLE 7: Scaling (Small / Medium / Large)"

    let testFeatures = [
        ("2 features", [| 0.5; 1.0 |])
        ("4 features", [| 0.5; 1.0; -0.3; 0.8 |])
        ("8 features", [| 0.1; 0.2; 0.3; 0.4; 0.5; 0.6; 0.7; 0.8 |])
    ]

    pr "%-12s | %6s | %5s" "Size" "Qubits" "Gates"
    pr "%s" (String.replicate 30 "-")

    for (desc, feat) in testFeatures do
        match FeatureMap.buildFeatureMap (ZZFeatureMap 1) feat with
        | Ok circ ->
            pr "%-12s | %6d | %5d" desc circ.QubitCount (gateCount circ)
            addResult (sprintf "7_scale_%d" feat.Length) circ.QubitCount (gateCount circ) true []
        | Error err ->
            pr "%-12s | Error: %s" desc err.Message

// â”€â”€ Output â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
let payload =
    Map.ofList [
        "script", box "FeatureMapExample.fsx"
        "timestamp", box (DateTime.UtcNow.ToString("o"))
        "features", box features
        "example", box exampleArg
        "backend", box (quantumBackend.Name)
        "results", box results
    ]

outputPath |> Option.iter (fun p -> Reporting.writeJson p payload)
csvPath    |> Option.iter (fun p ->
    Reporting.writeCsv p
        [ "example"; "qubits"; "gates"; "entangled" ]
        csvRows)

// â”€â”€ Usage hints â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
if not quiet && outputPath.IsNone && csvPath.IsNone && argv.Length = 0 then
    pr ""
    pr "Usage hints:"
    pr "  dotnet fsi FeatureMapExample.fsx -- --example 2"
    pr "  dotnet fsi FeatureMapExample.fsx -- --features \"0.1,0.5,0.9\""
    pr "  dotnet fsi FeatureMapExample.fsx -- --quiet --output r.json --csv r.csv"
    pr "  dotnet fsi FeatureMapExample.fsx -- --help"
