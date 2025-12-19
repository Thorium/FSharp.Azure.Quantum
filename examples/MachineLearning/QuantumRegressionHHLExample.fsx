// Quantum Regression via HHL Example
//
// Demonstrates training a simple linear regression model using the
// `FSharp.Azure.Quantum.MachineLearning.QuantumRegressionHHL` module.
//
// Run from repo root:
//   dotnet fsi "examples/MachineLearning/QuantumRegressionHHLExample.fsx"

//#r "nuget: FSharp.Azure.Quantum"
#r "../../src/FSharp.Azure.Quantum/bin/Debug/net10.0/FSharp.Azure.Quantum.dll"

open System
open FSharp.Azure.Quantum.Backends.LocalBackend
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.MachineLearning

let printSection title =
    printfn ""
    printfn "%s" (String.replicate 72 "=")
    printfn "%s" title
    printfn "%s" (String.replicate 72 "=")

printSection "Quantum Regression (HHL) Example"

// Local simulator backend
let backend = LocalBackend() :> IQuantumBackend

// Simple dataset: y = 2x + 1
let trainX = [| [| 1.0 |]; [| 2.0 |]; [| 3.0 |]; [| 4.0 |] |]
let trainY = [| 3.0; 5.0; 7.0; 9.0 |]

printfn "Training data (y = 2x + 1):"
trainX
|> Array.zip trainY
|> Array.iter (fun (y, x) -> printfn "  x=%A -> y=%.1f" x y)

printSection "Training"

let config : QuantumRegressionHHL.RegressionConfig = {
    TrainX = trainX
    TrainY = trainY
    EigenvalueQubits = 4
    MinEigenvalue = 0.01
    Backend = backend
    Shots = 5000
    FitIntercept = true
    Verbose = true
}

match QuantumRegressionHHL.train config with
| Error err ->
    printfn "Training failed: %s" err.Message
| Ok result ->
    printfn "\nTraining complete"
    printfn "  Weights: %A" (result.Weights |> Array.map (fun w -> Math.Round(w, 4)))
    printfn "  R²: %.4f" result.RSquared
    printfn "  MSE: %.6f" result.MSE
    printfn "  Success probability: %.4f" result.SuccessProbability

    printSection "Prediction"

    let xTest = [| 5.0 |]
    let yTrue = 11.0
    let yPred = QuantumRegressionHHL.predict result.Weights xTest result.HasIntercept
    printfn "Test point: x=%A" xTest
    printfn "  Predicted y: %.4f" yPred
    printfn "  Expected y:  %.4f" yTrue
    printfn "  Abs error:   %.4f" (abs (yPred - yTrue))
