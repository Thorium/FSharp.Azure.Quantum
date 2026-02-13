(*
    Modular Data -- Topological Quantum Computing
    ==============================================

    Computes S and T matrices (modular data) for anyon theories,
    verifies consistency relations, and compares quantum dimensions.

    Examples:
      1  Ising anyon modular data (Majorana zero modes)
      2  Fibonacci anyon modular data (universal TQC)
      3  SU(2)_3 anyon modular data
      4  Theory comparison table
      5  Key properties summary

    Run with: dotnet fsi ModularDataExample.fsx
              dotnet fsi ModularDataExample.fsx -- --example 2
              dotnet fsi ModularDataExample.fsx -- --quiet --output r.json --csv r.csv
*)

#r "../../src/FSharp.Azure.Quantum/bin/Debug/net10.0/FSharp.Azure.Quantum.dll"
#r "../../src/FSharp.Azure.Quantum.Topological/bin/Debug/net10.0/FSharp.Azure.Quantum.Topological.dll"
#load "../_common/Cli.fs"
#load "../_common/Data.fs"
#load "../_common/Reporting.fs"

open System
open System.Numerics
open FSharp.Azure.Quantum.Topological
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Examples.Common

// ---------------------------------------------------------------------------
// CLI
// ---------------------------------------------------------------------------
let argv = fsi.CommandLineArgs |> Array.skip 1
let args = Cli.parse argv

Cli.exitIfHelp "ModularDataExample.fsx" "Modular data (S/T matrices) for anyon theories"
    [ { Name = "example"; Description = "Which example: 1-5|all"; Default = Some "all" }
      { Name = "genus";   Description = "Max genus for degeneracy calc"; Default = Some "3" }
      { Name = "output";  Description = "Write results to JSON file"; Default = None }
      { Name = "csv";     Description = "Write results to CSV file";  Default = None }
      { Name = "quiet";   Description = "Suppress console output";    Default = None } ] args

let quiet      = Cli.hasFlag "quiet" args
let outputPath = Cli.tryGet "output" args
let csvPath    = Cli.tryGet "csv" args
let exChoice   = Cli.getOr "example" "all" args
let maxGenus   = Cli.getIntOr "genus" 3 args

let pr fmt = Printf.ksprintf (fun s -> if not quiet then printfn "%s" s) fmt
let shouldRun ex = exChoice = "all" || exChoice = string ex
let separator () = pr "%s" (String.replicate 60 "-")

// ---------------------------------------------------------------------------
// Quantum backend (Rule 1)
// ---------------------------------------------------------------------------
let quantumBackend = TopologicalUnifiedBackendFactory.createIsing 10

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------
let printMatrix name (matrix: Complex[,]) =
    let rows = Array2D.length1 matrix
    let cols = Array2D.length2 matrix
    pr "  %s (%dx%d):" name rows cols
    for i in 0 .. rows - 1 do
        let cells =
            [| for j in 0 .. cols - 1 do
                let c = matrix.[i, j]
                if abs c.Imaginary < 1e-10 then sprintf "%7.4f" c.Real
                else sprintf "%6.3f%+6.3fi" c.Real c.Imaginary |]
        pr "    %s" (String.Join("  ", cells))

let fmtCheck ok = if ok then "PASS" else "FAIL"

// Results accumulators
let mutable jsonResults : (string * obj) list = []
let mutable csvRows     : string list list    = []

// ---------------------------------------------------------------------------
// Example 1 -- Ising Anyon Modular Data
// ---------------------------------------------------------------------------
if shouldRun 1 then
    separator ()
    pr "EXAMPLE 1: Ising Anyons (Majorana Zero Modes)"
    separator ()

    match ModularData.computeModularData AnyonSpecies.Ising with
    | Error err ->
        pr "  Failed: %s" err.Message
    | Ok isingData ->
        pr "  Particles: [1, sigma, psi]"
        pr "  Central charge: c = %.2f" isingData.CentralCharge
        printMatrix "S-matrix (unlinking)" isingData.SMatrix
        printMatrix "T-matrix (twist)" isingData.TMatrix

        let sU = ModularData.verifySMatrixUnitary isingData.SMatrix
        let tD = ModularData.verifyTMatrixDiagonal isingData.TMatrix
        let st = ModularData.verifyModularSTRelation isingData.SMatrix isingData.TMatrix

        pr "  Consistency: S unitary=%s  T diagonal=%s  (ST)^3=%s" (fmtCheck sU) (fmtCheck tD) (fmtCheck st)

        match ModularData.totalQuantumDimension AnyonSpecies.Ising with
        | Ok totalD ->
            let dV = AnyonSpecies.quantumDimension AnyonSpecies.Vacuum
            let dS = AnyonSpecies.quantumDimension AnyonSpecies.Sigma
            let dP = AnyonSpecies.quantumDimension AnyonSpecies.Psi
            pr "  Quantum dims: d_1=%.4f  d_sigma=%.4f (sqrt2)  d_psi=%.4f" dV dS dP
            pr "  Total D = %.4f" totalD
        | Error _ -> ()

        pr "  Ground state degeneracies:"
        for g in 0 .. maxGenus do
            match ModularData.groundStateDegeneracy isingData g with
            | Ok dim -> pr "    g=%d: dim=%d" g dim
            | Error err -> pr "    g=%d: error %s" g err.Message

        jsonResults <- ("1_ising", box {| charge = isingData.CentralCharge; sUnitary = sU; tDiag = tD; stRel = st |}) :: jsonResults
        csvRows <- [ "1_ising"; sprintf "%.2f" isingData.CentralCharge; fmtCheck sU; fmtCheck tD; fmtCheck st ] :: csvRows

// ---------------------------------------------------------------------------
// Example 2 -- Fibonacci Anyon Modular Data
// ---------------------------------------------------------------------------
if shouldRun 2 then
    separator ()
    pr "EXAMPLE 2: Fibonacci Anyons (Universal TQC)"
    separator ()

    match ModularData.computeModularData AnyonSpecies.Fibonacci with
    | Error err ->
        pr "  Failed: %s" err.Message
    | Ok fibData ->
        let phi = (1.0 + sqrt 5.0) / 2.0
        pr "  Particles: [1, tau]"
        pr "  Central charge: c = %.4f" fibData.CentralCharge
        printMatrix "S-matrix" fibData.SMatrix
        printMatrix "T-matrix" fibData.TMatrix

        let sU = ModularData.verifySMatrixUnitary fibData.SMatrix
        let tD = ModularData.verifyTMatrixDiagonal fibData.TMatrix
        let st = ModularData.verifyModularSTRelation fibData.SMatrix fibData.TMatrix

        pr "  Consistency: S unitary=%s  T diagonal=%s  (ST)^3=%s" (fmtCheck sU) (fmtCheck tD) (fmtCheck st)

        let dTau = AnyonSpecies.quantumDimension AnyonSpecies.Tau
        pr "  d_tau = %.4f (phi = %.6f)" dTau phi

        match ModularData.totalQuantumDimension AnyonSpecies.Fibonacci with
        | Ok totalD -> pr "  Total D = sqrt(1+phi^2) = %.4f" totalD
        | Error _ -> ()

        pr "  Ground state degeneracies:"
        for g in 0 .. maxGenus do
            match ModularData.groundStateDegeneracy fibData g with
            | Ok dim -> pr "    g=%d: dim=%d" g dim
            | Error err -> pr "    g=%d: error %s" g err.Message

        jsonResults <- ("2_fibonacci", box {| charge = fibData.CentralCharge; sUnitary = sU; tDiag = tD; stRel = st |}) :: jsonResults
        csvRows <- [ "2_fibonacci"; sprintf "%.4f" fibData.CentralCharge; fmtCheck sU; fmtCheck tD; fmtCheck st ] :: csvRows

// ---------------------------------------------------------------------------
// Example 3 -- SU(2)_3 Modular Data
// ---------------------------------------------------------------------------
if shouldRun 3 then
    separator ()
    pr "EXAMPLE 3: SU(2)_3 Anyons"
    separator ()

    match ModularData.computeModularData (AnyonSpecies.SU2Level 3) with
    | Error err ->
        pr "  Failed: %s" err.Message
    | Ok su2Data ->
        pr "  Particles: j in {0, 1/2, 1, 3/2}"
        pr "  Central charge: c = %.4f" su2Data.CentralCharge
        printMatrix "S-matrix" su2Data.SMatrix

        pr "  T-matrix (diagonal phases):"
        for i in 0 .. 3 do
            let t = su2Data.TMatrix.[i, i]
            let phase = atan2 t.Imaginary t.Real
            pr "    T[%d,%d] = e^(i*%.4f) = %.4f%+.4fi" i i phase t.Real t.Imaginary

        let sU = ModularData.verifySMatrixUnitary su2Data.SMatrix
        let tD = ModularData.verifyTMatrixDiagonal su2Data.TMatrix
        let st = ModularData.verifyModularSTRelation su2Data.SMatrix su2Data.TMatrix

        pr "  Consistency: S unitary=%s  T diagonal=%s  (ST)^3=%s" (fmtCheck sU) (fmtCheck tD) (fmtCheck st)

        match AnyonSpecies.particles (AnyonSpecies.SU2Level 3) with
        | Ok particles ->
            pr "  Quantum dimensions:"
            particles |> List.iteri (fun i p ->
                pr "    d[j=%d/2] = %.4f" i (AnyonSpecies.quantumDimension p))
        | Error _ -> ()

        match ModularData.totalQuantumDimension (AnyonSpecies.SU2Level 3) with
        | Ok totalD -> pr "  Total D = %.4f" totalD
        | Error _ -> ()

        pr "  Ground state degeneracies:"
        for g in 0 .. min maxGenus 2 do
            match ModularData.groundStateDegeneracy su2Data g with
            | Ok dim -> pr "    g=%d: dim=%d" g dim
            | Error err -> pr "    g=%d: error %s" g err.Message

        jsonResults <- ("3_su2_3", box {| charge = su2Data.CentralCharge; sUnitary = sU; tDiag = tD; stRel = st |}) :: jsonResults
        csvRows <- [ "3_su2_3"; sprintf "%.4f" su2Data.CentralCharge; fmtCheck sU; fmtCheck tD; fmtCheck st ] :: csvRows

// ---------------------------------------------------------------------------
// Example 4 -- Theory comparison table
// ---------------------------------------------------------------------------
if shouldRun 4 then
    separator ()
    pr "EXAMPLE 4: Comparing Anyon Theories"
    separator ()

    let theories = [
        ("Ising",     AnyonSpecies.Ising,       "[1,s,p]",       "No (Clifford)")
        ("Fibonacci", AnyonSpecies.Fibonacci,    "[1,tau]",       "Yes")
        ("SU(2)_3",   AnyonSpecies.SU2Level 3,   "[0,1/2,1,3/2]", "No")
    ]

    pr "  %-11s  %-14s  %14s  %8s  %-14s" "Theory" "Particles" "Central Charge" "Total D" "Universal?"
    pr "  %s" (String.replicate 68 "-")

    for (name, theory, particles, universal) in theories do
        match ModularData.computeModularData theory, ModularData.totalQuantumDimension theory with
        | Ok data, Ok totalD ->
            pr "  %-11s  %-14s  %14.4f  %8.4f  %-14s" name particles data.CentralCharge totalD universal
        | _ -> ()

    jsonResults <- ("4_comparison", box {| theories = theories.Length |}) :: jsonResults
    csvRows <- [ "4_comparison"; string theories.Length ] :: csvRows

// ---------------------------------------------------------------------------
// Example 5 -- Key properties summary
// ---------------------------------------------------------------------------
if shouldRun 5 then
    separator ()
    pr "EXAMPLE 5: Key Properties of Modular Data"
    separator ()

    pr "  Mathematical structure:"
    pr "    S-matrix: Symmetric, unitary (S S' = I, S = S^T)"
    pr "    T-matrix: Diagonal, unitary (|theta_a| = 1)"
    pr "    Consistency: S^2 = C (charge conjugation)"
    pr "    Consistency: (ST)^3 = e^(iphi) S^2 (modular group PSL(2,Z))"
    pr ""
    pr "  Physical interpretation:"
    pr "    S_0a = d_a / D : quantum dimension normalization"
    pr "    theta_a = e^(2pi i h_a) : topological spin"
    pr "    D^2 = sum d_a^2 : total quantum dimension"
    pr ""
    pr "  Computational power:"
    pr "    Ising: Clifford gates only (needs magic states)"
    pr "    Fibonacci: Universal via braiding alone"
    pr "    SU(2)_k: k >= 3 needed for universality"

    jsonResults <- ("5_properties", box {| summary = "ok" |}) :: jsonResults
    csvRows <- [ "5_properties"; "ok" ] :: csvRows

// ---------------------------------------------------------------------------
// Output
// ---------------------------------------------------------------------------
if outputPath.IsSome then
    let payload =
        {| script    = "ModularDataExample.fsx"
           backend   = quantumBackend.Name
           timestamp = DateTime.UtcNow.ToString("o")
           example   = exChoice
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
    pr "  dotnet fsi ModularDataExample.fsx -- --example 2"
    pr "  dotnet fsi ModularDataExample.fsx -- --genus 5"
    pr "  dotnet fsi ModularDataExample.fsx -- --quiet --output r.json --csv r.csv"
    pr "  dotnet fsi ModularDataExample.fsx -- --help"
