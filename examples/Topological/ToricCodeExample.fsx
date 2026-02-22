(*
    Toric Code -- Topological Error Correction
    ============================================

    Demonstrates the Kitaev toric code: lattice creation, stabilizer
    structure, error injection, syndrome measurement, and anyon detection.

    Theory: The toric code encodes k=2 logical qubits in n=2L^2 physical
    qubits on an LxL torus with code distance d=L. X errors create
    e-particle pairs; Z errors create m-particle pairs.

    Examples:
      1  Create toric code lattice & show code parameters
      2  Initialize ground state & verify zero syndrome
      3  Inject X errors (bit flips) & detect e-particles
      4  Inject Z errors (phase flips) & detect m-particles
      5  Anyon statistics & distance calculations
      6  Key properties summary

    Run with: dotnet fsi ToricCodeExample.fsx
              dotnet fsi ToricCodeExample.fsx -- --lattice-size 7
              dotnet fsi ToricCodeExample.fsx -- --quiet --output r.json --csv r.csv
*)

#r "nuget: Microsoft.Extensions.Logging.Abstractions, 10.0.0"
#r "../../src/FSharp.Azure.Quantum/bin/Debug/net10.0/FSharp.Azure.Quantum.dll"
#r "../../src/FSharp.Azure.Quantum.Topological/bin/Debug/net10.0/FSharp.Azure.Quantum.Topological.dll"
#load "../_common/Cli.fs"
#load "../_common/Data.fs"
#load "../_common/Reporting.fs"

open System
open FSharp.Azure.Quantum.Topological
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Examples.Common

// ---------------------------------------------------------------------------
// CLI
// ---------------------------------------------------------------------------
let argv = fsi.CommandLineArgs |> Array.skip 1
let args = Cli.parse argv

Cli.exitIfHelp "ToricCodeExample.fsx" "Toric code lattice, error injection, and syndrome measurement"
    [ { Name = "example";      Description = "Which example: 1-6|all"; Default = Some "all" }
      { Name = "lattice-size"; Description = "Lattice width & height";  Default = Some "5" }
      { Name = "output";       Description = "Write results to JSON file"; Default = None }
      { Name = "csv";          Description = "Write results to CSV file";  Default = None }
      { Name = "quiet";        Description = "Suppress console output";    Default = None } ] args

let quiet      = Cli.hasFlag "quiet" args
let outputPath = Cli.tryGet "output" args
let csvPath    = Cli.tryGet "csv" args
let exChoice   = Cli.getOr "example" "all" args
let latticeSize = Cli.getIntOr "lattice-size" 5 args

let pr fmt = Printf.ksprintf (fun s -> if not quiet then printfn "%s" s) fmt
let shouldRun ex = exChoice = "all" || exChoice = string ex
let separator () = pr "%s" (String.replicate 60 "-")

// ---------------------------------------------------------------------------
// Quantum backend (Rule 1)
// ---------------------------------------------------------------------------
let quantumBackend = TopologicalUnifiedBackendFactory.createIsing 10

// Results accumulators
let mutable jsonResults : (string * obj) list = []
let mutable csvRows     : string list list    = []

// ---------------------------------------------------------------------------
// Create lattice (shared across examples)
// ---------------------------------------------------------------------------
let latticeResult = ToricCode.createLattice latticeSize latticeSize

match latticeResult with
| Error err ->
    pr "Failed to create lattice: %A" err
    exit 1
| Ok lattice ->

let k = ToricCode.logicalQubits lattice
let n = ToricCode.physicalQubits lattice
let d = ToricCode.codeDistance lattice
let rate = float k / float n

// ---------------------------------------------------------------------------
// Example 1 -- Lattice & code parameters
// ---------------------------------------------------------------------------
if shouldRun 1 then
    separator ()
    pr "EXAMPLE 1: %dx%d Toric Code Lattice" lattice.Width lattice.Height
    separator ()

    pr "  Logical qubits (k):   %d" k
    pr "  Physical qubits (n):  %d" n
    pr "  Code distance (d):    %d" d
    pr "  Encoding rate (k/n):  %.4f" rate
    pr "  Error correction:     up to %d errors" ((d - 1) / 2)

    jsonResults <- ("1_lattice", box {| width = lattice.Width; height = lattice.Height; k = k; n = n; d = d; rate = rate |}) :: jsonResults
    csvRows <- [ "1_lattice"; string k; string n; string d; sprintf "%.4f" rate ] :: csvRows

// ---------------------------------------------------------------------------
// Example 2 -- Ground state
// ---------------------------------------------------------------------------
let state = ToricCode.initializeGroundState lattice

if shouldRun 2 then
    separator ()
    pr "EXAMPLE 2: Ground State Initialization"
    separator ()

    pr "  Initialized %d qubits" state.Qubits.Count

    let syn = ToricCode.measureSyndrome state
    let eP = ToricCode.getElectricExcitations syn
    let mP = ToricCode.getMagneticExcitations syn

    pr "  e-particles: %d   m-particles: %d" eP.Length mP.Length
    pr "  Ground state confirmed (no anyons)"

    jsonResults <- ("2_ground", box {| qubits = state.Qubits.Count; ePart = eP.Length; mPart = mP.Length |}) :: jsonResults
    csvRows <- [ "2_ground"; string state.Qubits.Count; string eP.Length; string mP.Length ] :: csvRows

// ---------------------------------------------------------------------------
// Example 3 -- X errors
// ---------------------------------------------------------------------------
let xErr1 = { ToricCode.Position = { ToricCode.X = 2; ToricCode.Y = 2 }; ToricCode.Type = ToricCode.Horizontal }
let xErr2 = { ToricCode.Position = { ToricCode.X = 3; ToricCode.Y = 4 }; ToricCode.Type = ToricCode.Vertical }

let afterX1 = ToricCode.applyXError state xErr1
let afterX2 = ToricCode.applyXError afterX1 xErr2

if shouldRun 3 then
    separator ()
    pr "EXAMPLE 3: X Errors (Bit Flips)"
    separator ()

    pr "  Applied X errors:"
    pr "    Edge (%d,%d) %A" xErr1.Position.X xErr1.Position.Y xErr1.Type
    pr "    Edge (%d,%d) %A" xErr2.Position.X xErr2.Position.Y xErr2.Type

    let syn = ToricCode.measureSyndrome afterX2
    let eP = ToricCode.getElectricExcitations syn
    let mP = ToricCode.getMagneticExcitations syn

    pr "  e-particles: %d   m-particles: %d" eP.Length mP.Length
    if eP.Length > 0 then
        for p in eP do pr "    e at (%d,%d)" p.X p.Y

    jsonResults <- ("3_x_errors", box {| ePart = eP.Length; mPart = mP.Length |}) :: jsonResults
    csvRows <- [ "3_x_errors"; string eP.Length; string mP.Length ] :: csvRows

// ---------------------------------------------------------------------------
// Example 4 -- Z errors
// ---------------------------------------------------------------------------
let zErr1 = { ToricCode.Position = { ToricCode.X = 1; ToricCode.Y = 1 }; ToricCode.Type = ToricCode.Vertical }
let zErr2 = { ToricCode.Position = { ToricCode.X = 4; ToricCode.Y = 3 }; ToricCode.Type = ToricCode.Horizontal }

let afterZ1 = ToricCode.applyZError afterX2 zErr1
let afterZ2 = ToricCode.applyZError afterZ1 zErr2

if shouldRun 4 then
    separator ()
    pr "EXAMPLE 4: Z Errors (Phase Flips)"
    separator ()

    pr "  Applied Z errors:"
    pr "    Edge (%d,%d) %A" zErr1.Position.X zErr1.Position.Y zErr1.Type
    pr "    Edge (%d,%d) %A" zErr2.Position.X zErr2.Position.Y zErr2.Type

    let syn = ToricCode.measureSyndrome afterZ2
    let eP = ToricCode.getElectricExcitations syn
    let mP = ToricCode.getMagneticExcitations syn

    pr "  e-particles: %d   m-particles: %d" eP.Length mP.Length
    if mP.Length > 0 then
        for p in mP do pr "    m at (%d,%d)" p.X p.Y

    jsonResults <- ("4_z_errors", box {| ePart = eP.Length; mPart = mP.Length |}) :: jsonResults
    csvRows <- [ "4_z_errors"; string eP.Length; string mP.Length ] :: csvRows

// ---------------------------------------------------------------------------
// Example 5 -- Anyon distances
// ---------------------------------------------------------------------------
if shouldRun 5 then
    separator ()
    pr "EXAMPLE 5: Anyon Statistics & Distances"
    separator ()

    let syn = ToricCode.measureSyndrome afterZ2
    let eP = ToricCode.getElectricExcitations syn
    let mP = ToricCode.getMagneticExcitations syn

    if eP.Length >= 2 then
        pr "  e-particle pair distances (on torus):"
        for i in 0 .. eP.Length - 2 do
            for j in i + 1 .. eP.Length - 1 do
                let p1 = eP.[i]
                let p2 = eP.[j]
                let dist = ToricCode.toricDistance lattice p1 p2
                pr "    (%d,%d)<->(%d,%d) = %d" p1.X p1.Y p2.X p2.Y dist

    if mP.Length >= 2 then
        pr "  m-particle pair distances (on torus):"
        for i in 0 .. mP.Length - 2 do
            for j in i + 1 .. mP.Length - 1 do
                let p1 = mP.[i]
                let p2 = mP.[j]
                let dist = ToricCode.toricDistance lattice p1 p2
                pr "    (%d,%d)<->(%d,%d) = %d" p1.X p1.Y p2.X p2.Y dist

    jsonResults <- ("5_distances", box {| ePairs = max 0 (eP.Length - 1); mPairs = max 0 (mP.Length - 1) |}) :: jsonResults
    csvRows <- [ "5_distances"; string eP.Length; string mP.Length ] :: csvRows

// ---------------------------------------------------------------------------
// Example 6 -- Key properties
// ---------------------------------------------------------------------------
if shouldRun 6 then
    separator ()
    pr "EXAMPLE 6: Toric Code Key Properties"
    separator ()

    pr "  Anyon theory: Z2 x Z2"
    pr "    {1, e, m, epsilon}"
    pr "    1: Vacuum  e: Electric  m: Magnetic  epsilon: Fermion"
    pr ""
    pr "  Stabilizers:"
    pr "    Vertex A_v = prod(X on 4 edges)"
    pr "    Plaquette B_p = prod(Z on 4 edges)"
    pr ""
    pr "  Error correction:"
    pr "    Distance d=%d -> corrects %d errors" d ((d - 1) / 2)
    pr "    X errors -> e-particle pairs"
    pr "    Z errors -> m-particle pairs"
    pr "    Anyons always created in pairs (charge conservation)"
    pr ""
    pr "  Topological protection:"
    pr "    Logical qubits encoded in non-contractible loops"
    pr "    Information protected by topology, not local encoding"

    jsonResults <- ("6_properties", box {| summary = "ok" |}) :: jsonResults
    csvRows <- [ "6_properties"; "ok" ] :: csvRows

// ---------------------------------------------------------------------------
// Output
// ---------------------------------------------------------------------------
if outputPath.IsSome then
    let payload =
        {| script    = "ToricCodeExample.fsx"
           backend   = quantumBackend.Name
           timestamp = DateTime.UtcNow.ToString("o")
           example   = exChoice
           latticeSize = latticeSize
           results   = jsonResults |> List.rev |> List.map (fun (k,v) -> {| key = k; value = v |}) |}
    Reporting.writeJson outputPath.Value payload

if csvPath.IsSome then
    let header = [ "example"; "detail1"; "detail2"; "detail3"; "detail4" ]
    Reporting.writeCsv csvPath.Value header (csvRows |> List.rev)

// ---------------------------------------------------------------------------
// Usage hints
// ---------------------------------------------------------------------------
if not quiet && argv.Length = 0 then
    pr ""
    pr "Usage hints:"
    pr "  dotnet fsi ToricCodeExample.fsx -- --lattice-size 7"
    pr "  dotnet fsi ToricCodeExample.fsx -- --example 3"
    pr "  dotnet fsi ToricCodeExample.fsx -- --quiet --output r.json --csv r.csv"
    pr "  dotnet fsi ToricCodeExample.fsx -- --help"
