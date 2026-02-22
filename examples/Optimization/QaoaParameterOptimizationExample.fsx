#!/usr/bin/env dotnet fsi
// ============================================================================
// QAOA Parameter Optimization Example
// ============================================================================
//
// Demonstrates QAOA (Quantum Approximate Optimization Algorithm) parameter
// optimization for MaxCut. Compares optimization strategies: single-run,
// multi-start, and two-local pattern initialization.
//
// Extensible starting point for combinatorial optimization with QAOA.
//
// ============================================================================

#r "nuget: MathNet.Numerics, 5.0.0"
#r "nuget: Microsoft.Extensions.Logging.Abstractions, 10.0.0"
#r "../../src/FSharp.Azure.Quantum/bin/Debug/net10.0/FSharp.Azure.Quantum.dll"
#load "../_common/Cli.fs"
#load "../_common/Data.fs"
#load "../_common/Reporting.fs"

open System
open FSharp.Azure.Quantum.Core.QaoaCircuit
open FSharp.Azure.Quantum.Core.QaoaParameterOptimizer
open FSharp.Azure.Quantum.Core.CircuitAbstraction
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Backends.DWaveBackend
open FSharp.Azure.Quantum.Backends.DWaveTypes
open FSharp.Azure.Quantum.Examples.Common

// --- CLI ---
let argv = fsi.CommandLineArgs |> Array.skip 1
let args = Cli.parse argv

Cli.exitIfHelp
    "QaoaParameterOptimizationExample.fsx"
    "QAOA parameter optimization for MaxCut with strategy comparison"
    [ { Name = "layers"; Description = "QAOA depth (p layers)"; Default = Some "1" }
      { Name = "shots"; Description = "Measurement shots per evaluation"; Default = Some "500" }
      { Name = "max-iter"; Description = "Max optimizer iterations"; Default = Some "50" }
      { Name = "multi-starts"; Description = "Number of starts for multi-start strategy"; Default = Some "3" }
      { Name = "verify-shots"; Description = "Shots for final verification"; Default = Some "2000" }
      { Name = "seed"; Description = "Random seed for reproducibility"; Default = Some "42" }
      { Name = "output"; Description = "Write results to JSON file"; Default = None }
      { Name = "csv"; Description = "Write results to CSV file"; Default = None }
      { Name = "quiet"; Description = "Suppress console output"; Default = None } ]
    args

let quiet = Cli.hasFlag "quiet" args
let outputPath = Cli.tryGet "output" args
let csvPath = Cli.tryGet "csv" args
let p = Cli.getIntOr "layers" 1 args
let cliShots = Cli.getIntOr "shots" 500 args
let maxIter = Cli.getIntOr "max-iter" 50 args
let multiStarts = Cli.getIntOr "multi-starts" 3 args
let verifyShots = Cli.getIntOr "verify-shots" 2000 args
let seed = Cli.getIntOr "seed" 42 args

let pr fmt = Printf.ksprintf (fun s -> if not quiet then printfn "%s" s) fmt

// --- Quantum Backend (Rule 1) ---
// MockDWaveBackend implements IQuantumBackend; keep concrete type for Execute method
let dwaveBackend = createMockDWaveBackend Advantage_System6_1 (Some seed)
let quantumBackend = dwaveBackend :> IQuantumBackend

// ============================================================================
// STEP 1: Define MaxCut Problem
// ============================================================================

pr "--- Step 1: Define MaxCut Problem ---"
pr ""

// Triangle graph with equal weights
let edges = [ (0, 1, 1.0); (1, 2, 1.0); (0, 2, 1.0) ]
let numVertices = 3

let buildMaxCutHamiltonian (nVerts: int) (edgeList: (int * int * float) list) : ProblemHamiltonian =
    let diagonalTerms =
        [ 0 .. nVerts - 1 ]
        |> List.map (fun v ->
            let weight =
                edgeList
                |> List.filter (fun (u, w, _) -> u = v || w = v)
                |> List.sumBy (fun (_, _, w) -> w)
            { Coefficient = weight / 2.0; QubitsIndices = [| v |]; PauliOperators = [| PauliZ |] })

    let offDiagonalTerms =
        edgeList
        |> List.map (fun (u, v, w) ->
            { Coefficient = -w / 4.0; QubitsIndices = [| u; v |]; PauliOperators = [| PauliZ; PauliZ |] })

    { NumQubits = nVerts
      Terms = List.append diagonalTerms offDiagonalTerms |> List.toArray }

let problemHam = buildMaxCutHamiltonian numVertices edges

pr "  Graph: %d edges, %d vertices" edges.Length numVertices
pr "  Hamiltonian terms: %d" problemHam.Terms.Length
pr ""

// ============================================================================
// STEP 2: Compare Optimization Strategies
// ============================================================================

pr "--- Step 2: Compare Optimization Strategies ---"
pr ""

let makeConfig strategy initStrat =
    { defaultConfig with
        OptStrategy = strategy
        InitStrategy = initStrat
        NumShots = cliShots
        MaxIterations = maxIter
        RandomSeed = Some seed }

let strategies =
    [ ("SingleRun-Standard", makeConfig SingleRun StandardQAOA)
      (sprintf "MultiStart-%dx" multiStarts, makeConfig (MultiStart multiStarts) RandomUniform)
      ("SingleRun-TwoLocal", makeConfig SingleRun TwoLocalPattern) ]

let results =
    strategies
    |> List.map (fun (name, config) ->
        pr "  Running: %s ..." name
        let result = optimizeQaoaParameters problemHam p quantumBackend config
        pr "    Energy: %.6f  |  Converged: %b  |  Evaluations: %d" result.FinalEnergy result.Converged result.TotalEvaluations
        (name, result))

pr ""

// ============================================================================
// STEP 3: Compare Results
// ============================================================================

pr "--- Step 3: Results Comparison ---"
pr ""
pr "  %-25s | %12s | %9s | %11s" "Strategy" "Final Energy" "Converged" "Evaluations"
pr "  %s" (String.replicate 70 "-")

results
|> List.iter (fun (name, result) ->
    pr "  %-25s | %12.6f | %9b | %11d" name result.FinalEnergy result.Converged result.TotalEvaluations)

let (bestName, bestResult) =
    results |> List.minBy (fun (_, res) -> res.FinalEnergy)

pr ""
pr "  Best: %s (energy=%.6f)" bestName bestResult.FinalEnergy
pr ""

// ============================================================================
// STEP 4: Verify with Final Circuit
// ============================================================================

pr "--- Step 4: Verify Optimized Solution ---"
pr ""

let (optGamma, optBeta) = bestResult.OptimizedParameters.[0]
pr "  Optimized: gamma=%.4f, beta=%.4f" optGamma optBeta

let mixerHam = MixerHamiltonian.create numVertices
let optimalCircuit = QaoaCircuit.build problemHam mixerHam bestResult.OptimizedParameters
let circuitWrapper = QaoaCircuitWrapper(optimalCircuit) :> ICircuit

match dwaveBackend.Execute circuitWrapper verifyShots with
| Error e ->
    pr "  [ERROR] Verification failed: %A" e
| Ok execResult ->
    let counts =
        execResult.Measurements
        |> Array.countBy id
        |> Array.sortByDescending snd

    pr ""
    pr "  Top solutions (%d shots):" verifyShots
    pr "  %-12s | %6s | %11s | %9s" "Bitstring" "Count" "Probability" "Cut Value"
    pr "  %s" (String.replicate 48 "-")

    let topN = min 3 counts.Length

    counts
    |> Array.take topN
    |> Array.iter (fun (bitstring, count) ->
        let prob = float count / float execResult.NumShots
        let cutValue =
            edges
            |> List.filter (fun (u, v, _) -> bitstring.[u] <> bitstring.[v])
            |> List.sumBy (fun (_, _, w) -> w)
        let bitstringStr = String.Join("", bitstring)
        pr "  %-12s | %6d | %10.2f%% | %9.1f" bitstringStr count (prob * 100.0) cutValue)

    // Find maximum cut
    let maxCutSolution =
        counts
        |> Array.map (fun (bitstring, count) ->
            let cutValue =
                edges
                |> List.filter (fun (u, v, _) -> bitstring.[u] <> bitstring.[v])
                |> List.sumBy (fun (_, _, w) -> w)
            (bitstring, count, cutValue))
        |> Array.maxBy (fun (_, _, cutValue) -> cutValue)

    let (maxBitstring, maxCount, maxCut) = maxCutSolution
    let maxPartitionStr = String.Join("", maxBitstring)

    pr ""
    pr "  Maximum Cut: partition=%s, cut=%.1f/%.1f (%.1f%%)" maxPartitionStr maxCut (float edges.Length) (maxCut / float edges.Length * 100.0)
    pr "  Found in: %.1f%% of shots" (float maxCount / float execResult.NumShots * 100.0)
    pr ""

// --- JSON output ---

outputPath
|> Option.iter (fun path ->
    let payload =
        results
        |> List.map (fun (name, r) ->
            dict [
                "strategy", box name
                "finalEnergy", box r.FinalEnergy
                "converged", box r.Converged
                "totalEvaluations", box r.TotalEvaluations
                "gamma", box (fst r.OptimizedParameters.[0])
                "beta", box (snd r.OptimizedParameters.[0]) ])
    Reporting.writeJson path payload)

// --- CSV output ---

csvPath
|> Option.iter (fun path ->
    let header = [ "strategy"; "finalEnergy"; "converged"; "evaluations"; "gamma"; "beta" ]
    let rows =
        results
        |> List.map (fun (name, r) ->
            [ name; sprintf "%.6f" r.FinalEnergy; string r.Converged
              string r.TotalEvaluations
              sprintf "%.4f" (fst r.OptimizedParameters.[0])
              sprintf "%.4f" (snd r.OptimizedParameters.[0]) ])
    Reporting.writeCsv path header rows)

// --- Summary ---

if not quiet && outputPath.IsNone && csvPath.IsNone && (argv |> Array.isEmpty) then
    pr ""
    pr "Tip: Use --output results.json or --csv results.csv to export data."
    pr "     Use --layers 2 for deeper QAOA circuits."
    pr "     Use --shots 1000 for better energy estimates."
    pr "     Run with --help for all options."
