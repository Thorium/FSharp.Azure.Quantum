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
// The library comes from NuGet; `dotnet fsi --define:LOCAL_BUILD <script>` uses the repo's Debug build.
#if LOCAL_BUILD
#r "../../src/FSharp.Azure.Quantum/bin/Debug/net10.0/FSharp.Azure.Quantum.dll"
#else
#r "nuget: FSharp.Azure.Quantum"
#endif
#load "../_common/Cli.fs"
#load "../_common/Data.fs"
#load "../_common/Reporting.fs"

open System
open FSharp.Azure.Quantum.Core.QaoaCircuit
open FSharp.Azure.Quantum.Core.QaoaParameterOptimizer
open FSharp.Azure.Quantum.Core.CircuitAbstraction
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Backends.LocalBackend
open FSharp.Azure.Quantum.Examples.Common

// --- CLI ---
let argv = fsi.CommandLineArgs |> Array.skip 1
let args = Cli.parse argv

Cli.exitIfHelp
    "QaoaParameterOptimizationExample.fsx"
    "QAOA parameter optimization for MaxCut with strategy comparison"
    [
        {
            Name = "layers"
            Description = "QAOA depth (p layers)"
            Default = Some "1"
        }
        {
            Name = "shots"
            Description = "Measurement shots per evaluation"
            Default = Some "500"
        }
        {
            Name = "max-iter"
            Description = "Max optimizer iterations"
            Default = Some "50"
        }
        {
            Name = "multi-starts"
            Description = "Number of starts for multi-start strategy"
            Default = Some "3"
        }
        {
            Name = "verify-shots"
            Description = "Shots for final verification"
            Default = Some "2000"
        }
        {
            Name = "seed"
            Description = "Random seed for the optimizer's starting points"
            Default = Some "42"
        }
        {
            Name = "output"
            Description = "Write results to JSON file"
            Default = None
        }
        {
            Name = "csv"
            Description = "Write results to CSV file"
            Default = None
        }
        {
            Name = "quiet"
            Description = "Suppress console output"
            Default = None
        }
    ]
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

let pr fmt =
    Printf.ksprintf
        (fun s ->
            if not quiet then
                printfn "%s" s)
        fmt

// --- Quantum Backend (Rule 1) ---
// A gate-based backend: the QAOA angles are gate parameters. An annealer uses no angles,
// and tryOptimizeQaoaParameters refuses one.
let quantumBackend = LocalBackend() :> IQuantumBackend

// ============================================================================
// STEP 1: Define MaxCut Problem
// ============================================================================

pr "--- Step 1: Define MaxCut Problem ---"
pr ""

// Triangle graph with equal weights
let edges = [ (0, 1, 1.0); (1, 2, 1.0); (0, 2, 1.0) ]

[<Literal>]
let numVertices = 3

/// MaxCut as a minimisation. An edge (u, v) of weight w is cut when Z_u Z_v = -1, so the
/// cut weight is the sum of w (1 - Z_u Z_v) / 2 and the Hamiltonian sum of (w / 2) Z_u Z_v
/// is lowest where the cut is largest. The constant -w / 2 per edge is left out.
let buildMaxCutHamiltonian (nVerts: int) (edgeList: (int * int * float) list) : ProblemHamiltonian =
    {
        NumQubits = nVerts
        Terms =
            edgeList
            |> List.map (fun (u, v, w) ->
                {
                    Coefficient = w / 2.0
                    QubitsIndices = [| u; v |]
                    PauliOperators = [| PauliZ; PauliZ |]
                })
            |> List.toArray
    }

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
        RandomSeed = Some seed
    }

let strategies =
    [
        ("SingleRun-Standard", makeConfig SingleRun StandardQAOA)
        ($"MultiStart-%d{multiStarts}x", makeConfig (MultiStart multiStarts) RandomUniform)
        ("SingleRun-TwoLocal", makeConfig SingleRun TwoLocalPattern)
    ]

/// The strategies whose optimization returned a result. A backend error ends its strategy
/// and is printed; that strategy has no optimum to compare.
let results =
    strategies
    |> List.choose (fun (name, config) ->
        pr "  Running: %s ..." name

        match tryOptimizeQaoaParameters problemHam p quantumBackend config with
        | Ok result ->
            pr
                "    Energy: %.6f  |  Converged: %b  |  Evaluations: %d"
                result.FinalEnergy
                result.Converged
                result.TotalEvaluations

            Some(name, result)
        | Error err ->
            pr "    [ERROR] %s" err.Message
            None)

pr ""

if results.IsEmpty then
    eprintfn "Error: every optimization strategy failed on %s; nothing to compare." quantumBackend.Name
    exit 1

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

let (bestName, bestResult) = results |> List.minBy (fun (_, res) -> res.FinalEnergy)

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

let optimalCircuit =
    QaoaCircuit.build problemHam mixerHam bestResult.OptimizedParameters

let circuitWrapper = QaoaCircuitWrapper(optimalCircuit) :> ICircuit

match quantumBackend.ExecuteToState circuitWrapper with
| Error e -> pr "  [ERROR] Verification failed: %A" e
| Ok state ->
    let measurements = UnifiedBackend.measureState state verifyShots

    let counts = measurements |> Array.countBy id |> Array.sortByDescending snd

    pr ""
    pr "  Top solutions (%d shots):" verifyShots
    pr "  %-12s | %6s | %11s | %9s" "Bitstring" "Count" "Probability" "Cut Value"
    pr "  %s" (String.replicate 48 "-")

    let topN = min 3 counts.Length

    counts
    |> Array.take topN
    |> Array.iter (fun (bitstring, count) ->
        let prob = float count / float measurements.Length

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

    pr
        "  Maximum Cut: partition=%s, cut=%.1f/%.1f (%.1f%%)"
        maxPartitionStr
        maxCut
        (float edges.Length)
        (maxCut / float edges.Length * 100.0)

    pr "  Found in: %.1f%% of shots" (float maxCount / float measurements.Length * 100.0)
    pr ""

// --- JSON output ---

outputPath
|> Option.iter (fun path ->
    let payload =
        results
        |> List.map (fun (name, r) ->
            dict
                [
                    "strategy", box name
                    "finalEnergy", box r.FinalEnergy
                    "converged", box r.Converged
                    "totalEvaluations", box r.TotalEvaluations
                    "gamma", box (fst r.OptimizedParameters.[0])
                    "beta", box (snd r.OptimizedParameters.[0])
                ])

    Reporting.writeJson path payload)

// --- CSV output ---

csvPath
|> Option.iter (fun path ->
    let header =
        [ "strategy"; "finalEnergy"; "converged"; "evaluations"; "gamma"; "beta" ]

    let rows =
        results
        |> List.map (fun (name, r) ->
            [
                name
                $"%.6f{r.FinalEnergy}"
                string r.Converged
                string r.TotalEvaluations
                sprintf "%.4f" (fst r.OptimizedParameters.[0])
                sprintf "%.4f" (snd r.OptimizedParameters.[0])
            ])

    Reporting.writeCsv path header rows)

// --- Summary ---

if not quiet && outputPath.IsNone && csvPath.IsNone && (argv |> Array.isEmpty) then
    pr ""
    pr "Tip: Use --output results.json or --csv results.csv to export data."
    pr "     Use --layers 2 for deeper QAOA circuits."
    pr "     Use --shots 1000 for better energy estimates."
    pr "     Run with --help for all options."
