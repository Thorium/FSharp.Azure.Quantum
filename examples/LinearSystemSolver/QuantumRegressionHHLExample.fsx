// Quantum Regression via HHL Example
//
// Demonstrates training linear regression models using the
// FSharp.Azure.Quantum.MachineLearning.QuantumRegressionHHL module.
//
// Examples:
//   1. Simple regression (y = 2x + 1)
//   2. Multi-feature regression (y = 3x1 + 2x2 + 1)
//   3. Batch prediction on test set
//
// Run from repo root:
//   dotnet fsi examples/LinearSystemSolver/QuantumRegressionHHLExample.fsx
//   dotnet fsi examples/LinearSystemSolver/QuantumRegressionHHLExample.fsx -- --example 2
//   dotnet fsi examples/LinearSystemSolver/QuantumRegressionHHLExample.fsx -- --quiet --output r.json

//#r "nuget: FSharp.Azure.Quantum"
#r "../../src/FSharp.Azure.Quantum/bin/Debug/net10.0/FSharp.Azure.Quantum.dll"
#r "nuget: Microsoft.Extensions.Logging.Abstractions, 10.0.0"

#load "../_common/Cli.fs"
#load "../_common/Data.fs"
#load "../_common/Reporting.fs"

open System
open FSharp.Azure.Quantum.Backends.LocalBackend
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.MachineLearning
open FSharp.Azure.Quantum.Examples.Common

// ── CLI ──────────────────────────────────────────────────────────────
let argv = fsi.CommandLineArgs |> Array.skip 1
let args = Cli.parse argv

Cli.exitIfHelp
    "QuantumRegressionHHLExample.fsx"
    "Quantum linear regression via HHL algorithm"
    [ { Name = "example";          Description = "Which example: 1|2|3|all"; Default = Some "all" }
      { Name = "eigenvalue-qubits"; Description = "QPE qubits for HHL";     Default = Some "4" }
      { Name = "shots";            Description = "Simulation shots";         Default = Some "5000" }
      { Name = "min-eigenvalue";   Description = "Min eigenvalue threshold"; Default = Some "0.01" }
      { Name = "output";           Description = "Write results to JSON";    Default = None }
      { Name = "csv";              Description = "Write results to CSV";     Default = None }
      { Name = "quiet";            Description = "Suppress console output";  Default = None } ]
    args

let quiet       = Cli.hasFlag "quiet" args
let outputPath  = Cli.tryGet "output" args
let csvPath     = Cli.tryGet "csv" args
let exampleArg  = Cli.getOr "example" "all" args
let eqQubits    = Cli.getIntOr "eigenvalue-qubits" 4 args
let cliShots    = Cli.getIntOr "shots" 5000 args
let cliMinEigen = Cli.getFloatOr "min-eigenvalue" 0.01 args

let pr fmt = Printf.ksprintf (fun s -> if not quiet then printfn "%s" s) fmt

let section title =
    pr ""
    pr "%s" (String.replicate 68 "-")
    pr "%s" title
    pr "%s" (String.replicate 68 "-")

// ── Quantum Backend (Rule 1) ────────────────────────────────────────
let quantumBackend = LocalBackend() :> IQuantumBackend

// ── Result accumulator ──────────────────────────────────────────────
let mutable results : Map<string, obj> list = []
let mutable csvRows : string list list = []

let shouldRun ex =
    exampleArg = "all" || exampleArg = string ex

// ── EXAMPLE 1: Simple regression y = 2x + 1 ────────────────────────
if shouldRun 1 then
    section "EXAMPLE 1: Simple Linear Regression (y = 2x + 1)"

    let trainX = [| [| 1.0 |]; [| 2.0 |]; [| 3.0 |]; [| 4.0 |] |]
    let trainY = [| 3.0; 5.0; 7.0; 9.0 |]

    pr "Training data:"
    trainX
    |> Array.zip trainY
    |> Array.iter (fun (y, x) -> pr "  x=%A -> y=%.1f" x y)

    let config : QuantumRegressionHHL.RegressionConfig =
        { TrainX = trainX
          TrainY = trainY
          EigenvalueQubits = eqQubits
          MinEigenvalue = cliMinEigen
          Backend = quantumBackend
          Shots = cliShots
          FitIntercept = true
          Verbose = not quiet
          Logger = None }

    match QuantumRegressionHHL.train config with
    | Error err ->
        pr "Training FAILED: %s" err.Message
    | Ok result ->
        pr ""
        pr "Training complete"
        pr "  Weights:  %A" (result.Weights |> Array.map (fun w -> Math.Round(w, 4)))
        pr "  R²:       %.4f" result.RSquared
        pr "  MSE:      %.6f" result.MSE
        pr "  Success:  %.4f" result.SuccessProbability

        // Single prediction
        let xTest = [| 5.0 |]
        let yTrue = 11.0
        let yPred = QuantumRegressionHHL.predict result.Weights xTest result.HasIntercept
        pr ""
        pr "Prediction: x=[5.0]"
        pr "  Predicted: %.4f" yPred
        pr "  Expected:  %.4f" yTrue
        pr "  Error:     %.4f" (abs (yPred - yTrue))

        results <- results @ [
            Map.ofList [
                "example", box "1_simple_regression"
                "weights", box (result.Weights |> Array.map (fun w -> Math.Round(w, 4)))
                "r_squared", box (sprintf "%.4f" result.RSquared)
                "mse", box (sprintf "%.6f" result.MSE)
                "success_probability", box (sprintf "%.4f" result.SuccessProbability)
                "prediction_x5", box (sprintf "%.4f" yPred)
                "prediction_error", box (sprintf "%.4f" (abs (yPred - yTrue)))
            ]
        ]
        csvRows <- csvRows @ [
            [ "1_simple"; sprintf "%.4f" result.RSquared; sprintf "%.6f" result.MSE
              sprintf "%.4f" result.SuccessProbability
              (result.Weights |> Array.map (fun w -> sprintf "%.4f" w) |> String.concat ";")
              sprintf "%.4f" yPred; sprintf "%.4f" (abs (yPred - yTrue)) ]
        ]

// ── EXAMPLE 2: Multi-feature regression y = 3x1 + 2x2 + 1 ─────────
if shouldRun 2 then
    section "EXAMPLE 2: Multi-Feature Regression (y = 3x1 + 2x2 + 1)"

    let trainX =
        [| [| 1.0; 1.0 |]
           [| 2.0; 1.0 |]
           [| 1.0; 2.0 |]
           [| 3.0; 2.0 |] |]
    let trainY = [| 6.0; 9.0; 8.0; 14.0 |] // 3*x1 + 2*x2 + 1

    pr "Training data (y = 3*x1 + 2*x2 + 1):"
    for i in 0 .. trainX.Length - 1 do
        pr "  x1=%.0f, x2=%.0f -> y=%.1f" trainX.[i].[0] trainX.[i].[1] trainY.[i]

    let config : QuantumRegressionHHL.RegressionConfig =
        { TrainX = trainX
          TrainY = trainY
          EigenvalueQubits = eqQubits
          MinEigenvalue = cliMinEigen
          Backend = quantumBackend
          Shots = cliShots
          FitIntercept = true
          Verbose = not quiet
          Logger = None }

    match QuantumRegressionHHL.train config with
    | Error err ->
        pr "Training FAILED: %s" err.Message
    | Ok result ->
        pr ""
        pr "Training complete"
        pr "  Weights:      %A" (result.Weights |> Array.map (fun w -> Math.Round(w, 4)))
        pr "  R²:           %.4f" result.RSquared
        pr "  MSE:          %.6f" result.MSE
        pr "  Success:      %.4f" result.SuccessProbability
        pr "  Features:     %d" result.NumFeatures
        pr "  Samples:      %d" result.NumSamples
        if result.ConditionNumber.IsSome then
            pr "  Condition #:  %.2f" result.ConditionNumber.Value

        // Predict a new point
        let xTest = [| 2.0; 3.0 |]
        let yTrue = 3.0 * 2.0 + 2.0 * 3.0 + 1.0 // = 13.0
        let yPred = QuantumRegressionHHL.predict result.Weights xTest result.HasIntercept
        pr ""
        pr "Prediction: x1=2, x2=3"
        pr "  Predicted: %.4f" yPred
        pr "  Expected:  %.4f" yTrue
        pr "  Error:     %.4f" (abs (yPred - yTrue))

        results <- results @ [
            Map.ofList [
                "example", box "2_multi_feature"
                "weights", box (result.Weights |> Array.map (fun w -> Math.Round(w, 4)))
                "r_squared", box (sprintf "%.4f" result.RSquared)
                "mse", box (sprintf "%.6f" result.MSE)
                "success_probability", box (sprintf "%.4f" result.SuccessProbability)
                "num_features", box result.NumFeatures
                "prediction_x2_3", box (sprintf "%.4f" yPred)
                "prediction_error", box (sprintf "%.4f" (abs (yPred - yTrue)))
            ]
        ]
        csvRows <- csvRows @ [
            [ "2_multi"; sprintf "%.4f" result.RSquared; sprintf "%.6f" result.MSE
              sprintf "%.4f" result.SuccessProbability
              (result.Weights |> Array.map (fun w -> sprintf "%.4f" w) |> String.concat ";")
              sprintf "%.4f" yPred; sprintf "%.4f" (abs (yPred - yTrue)) ]
        ]

// ── EXAMPLE 3: Batch prediction on test set ─────────────────────────
if shouldRun 3 then
    section "EXAMPLE 3: Batch Prediction on Test Set"

    // Train on same simple model
    let trainX = [| [| 1.0 |]; [| 2.0 |]; [| 3.0 |]; [| 4.0 |] |]
    let trainY = [| 3.0; 5.0; 7.0; 9.0 |]

    pr "Training on y = 2x + 1 (4 samples)..."

    let config : QuantumRegressionHHL.RegressionConfig =
        { TrainX = trainX
          TrainY = trainY
          EigenvalueQubits = eqQubits
          MinEigenvalue = cliMinEigen
          Backend = quantumBackend
          Shots = cliShots
          FitIntercept = true
          Verbose = false
          Logger = None }

    match QuantumRegressionHHL.train config with
    | Error err ->
        pr "Training FAILED: %s" err.Message
    | Ok result ->
        pr "  R²:  %.4f" result.RSquared
        pr "  MSE: %.6f" result.MSE

        // Batch predictions
        let testX = [| [| 5.0 |]; [| 6.0 |]; [| 7.0 |]; [| 10.0 |] |]
        let testY = [| 11.0; 13.0; 15.0; 21.0 |]

        let preds = QuantumRegressionHHL.predictBatch result.Weights testX result.HasIntercept

        pr ""
        pr "Batch predictions on test set:"
        pr "  %-8s %-12s %-12s %-12s" "x" "predicted" "expected" "error"
        pr "  %s" (String.replicate 48 "-")
        for i in 0 .. testX.Length - 1 do
            let err = abs (preds.[i] - testY.[i])
            pr "  %-8.1f %-12.4f %-12.4f %-12.4f" testX.[i].[0] preds.[i] testY.[i] err

        let avgError =
            Array.map2 (fun p t -> abs (p - t)) preds testY |> Array.average
        let maxError =
            Array.map2 (fun p t -> abs (p - t)) preds testY |> Array.max
        pr ""
        pr "Summary:"
        pr "  Average error: %.4f" avgError
        pr "  Max error:     %.4f" maxError

        results <- results @ [
            Map.ofList [
                "example", box "3_batch_prediction"
                "r_squared", box (sprintf "%.4f" result.RSquared)
                "mse", box (sprintf "%.6f" result.MSE)
                "test_points", box testX.Length
                "avg_error", box (sprintf "%.4f" avgError)
                "max_error", box (sprintf "%.4f" maxError)
            ]
        ]
        for i in 0 .. testX.Length - 1 do
            let err = abs (preds.[i] - testY.[i])
            csvRows <- csvRows @ [
                [ sprintf "3_batch_x%.0f" testX.[i].[0]
                  sprintf "%.4f" result.RSquared; sprintf "%.6f" result.MSE
                  sprintf "%.4f" result.SuccessProbability
                  (result.Weights |> Array.map (fun w -> sprintf "%.4f" w) |> String.concat ";")
                  sprintf "%.4f" preds.[i]; sprintf "%.4f" err ]
            ]

// ── Output ───────────────────────────────────────────────────────────
let payload =
    Map.ofList [
        "script", box "QuantumRegressionHHLExample.fsx"
        "timestamp", box (DateTime.UtcNow.ToString("o"))
        "eigenvalue_qubits", box eqQubits
        "shots", box cliShots
        "min_eigenvalue", box cliMinEigen
        "example", box exampleArg
        "results", box results
    ]

outputPath |> Option.iter (fun p -> Reporting.writeJson p payload)
csvPath    |> Option.iter (fun p ->
    Reporting.writeCsv p
        [ "example"; "r_squared"; "mse"; "success_probability"; "weights"; "prediction"; "error" ]
        csvRows)

// ── Usage hints ──────────────────────────────────────────────────────
if not quiet && outputPath.IsNone && csvPath.IsNone && argv.Length = 0 then
    pr ""
    pr "Usage hints:"
    pr "  dotnet fsi QuantumRegressionHHLExample.fsx -- --example 2"
    pr "  dotnet fsi QuantumRegressionHHLExample.fsx -- --eigenvalue-qubits 6 --shots 10000"
    pr "  dotnet fsi QuantumRegressionHHLExample.fsx -- --quiet --output r.json --csv r.csv"
    pr "  dotnet fsi QuantumRegressionHHLExample.fsx -- --help"
