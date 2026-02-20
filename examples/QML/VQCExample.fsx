(*
    Variational Quantum Classifier (VQC) Example
    ============================================

    End-to-end quantum machine learning: train, predict, evaluate.
    Uses parameter shift rule for quantum gradients on LocalBackend.

    Run with: dotnet fsi VQCExample.fsx
              dotnet fsi VQCExample.fsx -- --epochs 10 --learning-rate 0.05
              dotnet fsi VQCExample.fsx -- --example 4 --quiet --output r.json

    References:
      [1] Havlicek et al., Nature 567, 209-212 (2019)
      [2] Schuld & Petruccione, "ML with Quantum Computers" (2021)
      [3] Mitarai et al., Phys. Rev. A 98, 032309 (2018)
*)

#r "../../src/FSharp.Azure.Quantum/bin/Debug/net10.0/FSharp.Azure.Quantum.dll"
#r "nuget: Microsoft.Extensions.Logging.Abstractions, 10.0.0"
#load "../_common/Cli.fs"
#load "../_common/Data.fs"
#load "../_common/Reporting.fs"

open System
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.MachineLearning
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Backends.LocalBackend
open FSharp.Azure.Quantum.Examples.Common

// ---------------------------------------------------------------------------
// CLI
// ---------------------------------------------------------------------------
let argv = fsi.CommandLineArgs |> Array.skip 1
let args = Cli.parse argv

Cli.exitIfHelp "VQCExample.fsx" "End-to-end Variational Quantum Classifier (train, predict, evaluate)"
    [ { Name = "example";       Description = "Which example: 1-8|all";          Default = Some "all" }
      { Name = "epochs";        Description = "Max training epochs";             Default = Some "5" }
      { Name = "learning-rate"; Description = "Optimiser learning rate";         Default = Some "0.1" }
      { Name = "shots";         Description = "Shots per circuit evaluation";    Default = Some "1000" }
      { Name = "ansatz-depth";  Description = "Variational form depth";          Default = Some "2" }
      { Name = "output";        Description = "Write results to JSON file";      Default = None }
      { Name = "csv";           Description = "Write results to CSV file";       Default = None }
      { Name = "quiet";         Description = "Suppress console output";         Default = None } ] args

let quiet      = Cli.hasFlag "quiet" args
let outputPath = Cli.tryGet "output" args
let csvPath    = Cli.tryGet "csv" args
let exChoice   = Cli.getOr "example" "all" args
let cliEpochs  = Cli.getIntOr "epochs" 5 args
let cliLR      = Cli.getFloatOr "learning-rate" 0.1 args
let cliShots   = Cli.getIntOr "shots" 1000 args
let cliDepth   = Cli.getIntOr "ansatz-depth" 2 args

let pr fmt = Printf.ksprintf (fun s -> if not quiet then printfn "%s" s) fmt

let shouldRun ex =
    exChoice = "all" || exChoice = string ex

let separator () =
    pr "%s" (String.replicate 60 "-")

let fmt (x: float) = sprintf "%.4f" x
let fmtArr (xs: float array) =
    xs |> Array.map fmt |> String.concat ", " |> sprintf "[%s]"

// ---------------------------------------------------------------------------
// Quantum backend (Rule 1)
// ---------------------------------------------------------------------------
let quantumBackend = LocalBackend() :> IQuantumBackend

// ---------------------------------------------------------------------------
// Shared configuration
// ---------------------------------------------------------------------------
let featureMap       = AngleEncoding
let variationalForm  = RealAmplitudes cliDepth

let config : VQC.TrainingConfig =
    { LearningRate          = cliLR
      MaxEpochs             = cliEpochs
      ConvergenceThreshold  = 0.001
      Shots                 = cliShots
      Verbose               = false
      Optimizer             = VQC.Adam { LearningRate = cliLR; Beta1 = 0.9; Beta2 = 0.999; Epsilon = 1e-8 }
      ProgressReporter      = None
      Logger                = None }

// XOR-like dataset
let trainData = [|
    [| 0.1; 0.1 |]; [| 0.2; 0.1 |]; [| 0.1; 0.2 |]   // class 0
    [| 0.9; 0.9 |]; [| 0.8; 0.9 |]; [| 0.9; 0.8 |]
    [| 0.1; 0.9 |]; [| 0.2; 0.8 |]; [| 0.1; 0.8 |]   // class 1
    [| 0.9; 0.1 |]; [| 0.8; 0.2 |]; [| 0.9; 0.2 |] |]
let trainLabels = [| 0;0;0; 0;0;0; 1;1;1; 1;1;1 |]

let testData   = [| [|0.15;0.15|]; [|0.85;0.85|]; [|0.15;0.85|]; [|0.85;0.15|] |]
let testLabels = [| 0; 0; 1; 1 |]

let numQubits     = trainData.[0].Length
let initialParams = Array.init (numQubits * 2) (fun _ -> 0.1)

// Mutable results accumulator
let mutable jsonResults : (string * obj) list = []
let mutable csvRows     : string list list    = []

// ---------------------------------------------------------------------------
// Example 1 — Setup & Architecture
// ---------------------------------------------------------------------------
if shouldRun 1 then
    separator ()
    pr "EXAMPLE 1: Setup & Architecture"
    separator ()
    pr "Backend:          LocalBackend (quantum simulator)"
    pr "Feature Map:      AngleEncoding (Ry rotations)"
    pr "Variational Form: RealAmplitudes (depth=%d)" cliDepth
    pr "Learning Rate:    %s" (fmt config.LearningRate)
    pr "Max Epochs:       %d" config.MaxEpochs
    pr "Shots per Circuit: %d" config.Shots
    pr "Optimiser:        Adam"

    jsonResults <- ("1_setup", box {| backend = "LocalBackend"
                                      featureMap = "AngleEncoding"
                                      ansatz = sprintf "RealAmplitudes(depth=%d)" cliDepth
                                      learningRate = config.LearningRate
                                      maxEpochs = config.MaxEpochs
                                      shots = config.Shots |}) :: jsonResults

// ---------------------------------------------------------------------------
// Example 2 — Dataset
// ---------------------------------------------------------------------------
if shouldRun 2 then
    separator ()
    pr "EXAMPLE 2: Dataset (XOR-like binary classification)"
    separator ()
    let c0 = trainLabels |> Array.filter ((=) 0) |> Array.length
    let c1 = trainLabels |> Array.filter ((=) 1) |> Array.length
    pr "Training samples: %d  (class 0: %d, class 1: %d)" trainData.Length c0 c1
    pr "Features:         %d" numQubits
    pr "Test samples:     %d" testData.Length
    pr ""
    pr "Sample points:"
    pr "  Class 0: %s" (fmtArr trainData.[0])
    pr "  Class 0: %s" (fmtArr trainData.[5])
    pr "  Class 1: %s" (fmtArr trainData.[6])
    pr "  Class 1: %s" (fmtArr trainData.[11])

    jsonResults <- ("2_dataset", box {| trainSamples = trainData.Length
                                        testSamples = testData.Length
                                        features = numQubits
                                        class0 = c0; class1 = c1 |}) :: jsonResults

// ---------------------------------------------------------------------------
// Example 3 — Training
// ---------------------------------------------------------------------------
// Train once and share result across examples 3-6
let trainResult =
    if shouldRun 3 || shouldRun 4 || shouldRun 5 || shouldRun 6 then
        Some (VQC.train quantumBackend featureMap variationalForm initialParams trainData trainLabels config)
    else None

if shouldRun 3 then
    separator ()
    pr "EXAMPLE 3: Training (parameter shift rule)"
    separator ()
    match trainResult with
    | Some (Ok result) ->
        pr "Training completed"
        pr "  Final params:    %s" (fmtArr result.Parameters)
        pr "  Train accuracy:  %s" (fmt result.TrainAccuracy)
        pr "  Epochs:          %d" result.Epochs
        pr "  Converged:       %s" (if result.Converged then "yes" else "no (max epochs)")
        pr ""
        pr "Loss history:"
        result.LossHistory
        |> List.take (min 10 result.LossHistory.Length)
        |> List.iteri (fun i l -> pr "  Epoch %2d: %s" (i+1) (fmt l))
        if result.LossHistory.Length > 10 then
            pr "  ..."
            pr "  Epoch %2d: %s" result.LossHistory.Length (fmt (List.last result.LossHistory))

        jsonResults <- ("3_training", box {| accuracy = result.TrainAccuracy
                                             epochs = result.Epochs
                                             converged = result.Converged
                                             finalParams = result.Parameters |}) :: jsonResults
        csvRows <- [ "3_training"; fmt result.TrainAccuracy; string result.Epochs;
                      string result.Converged; fmtArr result.Parameters ] :: csvRows

    | Some (Error err) ->
        pr "Training failed: %s" err.Message
    | None -> ()

// ---------------------------------------------------------------------------
// Example 4 — Predictions
// ---------------------------------------------------------------------------
if shouldRun 4 then
    separator ()
    pr "EXAMPLE 4: Predictions on test set"
    separator ()
    match trainResult with
    | Some (Ok result) ->
        testData |> Array.iteri (fun i sample ->
            match VQC.predict quantumBackend featureMap variationalForm result.Parameters sample config.Shots with
            | Ok pred ->
                let mark = if pred.Label = testLabels.[i] then "correct" else "wrong"
                pr "Sample %d: %s  -> predicted %d (prob %s)  actual %d  [%s]"
                    (i+1) (fmtArr sample) pred.Label (fmt pred.Probability) testLabels.[i] mark

                csvRows <- [ sprintf "4_predict_%d" (i+1); string pred.Label;
                             fmt pred.Probability; string testLabels.[i]; mark ] :: csvRows
            | Error err ->
                pr "Sample %d: prediction failed — %s" (i+1) err.Message)
    | Some (Error _) -> pr "(skipped — training failed)"
    | None -> ()

// ---------------------------------------------------------------------------
// Example 5 — Evaluation
// ---------------------------------------------------------------------------
if shouldRun 5 then
    separator ()
    pr "EXAMPLE 5: Model evaluation"
    separator ()
    match trainResult with
    | Some (Ok result) ->
        let showEval label data labels =
            match VQC.evaluate quantumBackend featureMap variationalForm result.Parameters data labels config.Shots with
            | Ok acc ->
                pr "%s accuracy: %s (%.1f%%)" label (fmt acc) (acc * 100.0)
                acc
            | Error err ->
                pr "%s evaluation failed: %s" label err.Message
                0.0
        let trainAcc = showEval "Training set" trainData trainLabels
        let testAcc  = showEval "Test set"     testData  testLabels

        jsonResults <- ("5_evaluation", box {| trainAccuracy = trainAcc
                                               testAccuracy = testAcc |}) :: jsonResults
        csvRows <- [ "5_evaluation"; fmt trainAcc; fmt testAcc; ""; "" ] :: csvRows

    | Some (Error _) -> pr "(skipped — training failed)"
    | None -> ()

// ---------------------------------------------------------------------------
// Example 6 — Confusion matrix
// ---------------------------------------------------------------------------
if shouldRun 6 then
    separator ()
    pr "EXAMPLE 6: Confusion matrix (test set)"
    separator ()
    match trainResult with
    | Some (Ok result) ->
        match VQC.confusionMatrix quantumBackend featureMap variationalForm result.Parameters testData testLabels config.Shots with
        | Ok cm ->
            pr "                 Predicted"
            pr "              Class 0  Class 1"
            pr "Actual  C0      %2d       %2d" cm.TrueNegatives cm.FalsePositives
            pr "        C1      %2d       %2d" cm.FalseNegatives cm.TruePositives
            pr ""
            let prec = VQC.precision cm
            let rec' = VQC.recall cm
            let f1   = VQC.f1Score cm
            let acc  = float (cm.TruePositives + cm.TrueNegatives) / float testData.Length
            pr "Accuracy:  %s  Precision: %s  Recall: %s  F1: %s"
                (fmt acc) (fmt prec) (fmt rec') (fmt f1)

            jsonResults <- ("6_confusion", box {| tp = cm.TruePositives; tn = cm.TrueNegatives
                                                  fp = cm.FalsePositives; fn = cm.FalseNegatives
                                                  accuracy = acc; precision = prec
                                                  recall = rec'; f1 = f1 |}) :: jsonResults
            csvRows <- [ "6_confusion"; fmt acc; fmt prec; fmt rec'; fmt f1 ] :: csvRows

        | Error err -> pr "Confusion matrix failed: %s" err.Message
    | Some (Error _) -> pr "(skipped — training failed)"
    | None -> ()

// ---------------------------------------------------------------------------
// Example 7 — Circuit analysis
// ---------------------------------------------------------------------------
if shouldRun 7 then
    separator ()
    pr "EXAMPLE 7: Circuit analysis"
    separator ()
    let numParams = AnsatzHelpers.parameterCount variationalForm numQubits
    let fmCircuit = FeatureMap.angleEncoding trainData.[0]
    let fmGates   = fmCircuit.Gates.Length

    pr "Qubits:           %d" numQubits
    pr "Parameters:       %d" numParams
    pr "Feature-map gates: %d" fmGates

    match VariationalForms.buildVariationalForm variationalForm (Array.create numParams 0.0) numQubits with
    | Ok ac ->
        let aGates = ac.Gates.Length
        pr "Ansatz gates:      %d" aGates
        pr "Total gates:       %d" (fmGates + aGates)
        pr ""
        let gradsPerEpoch   = numParams * 2
        let circPerEpoch    = trainData.Length + gradsPerEpoch * trainData.Length
        pr "Circuits/epoch:    %d  (forward + %d gradient evals)" circPerEpoch gradsPerEpoch
        match trainResult with
        | Some (Ok r) -> pr "Total circuits:    %d  (%d epochs)" (circPerEpoch * r.Epochs) r.Epochs
        | _ -> ()

        jsonResults <- ("7_circuit", box {| qubits = numQubits; vParams = numParams
                                            featureMapGates = fmGates; ansatzGates = aGates
                                            totalGates = fmGates + aGates |}) :: jsonResults
        csvRows <- [ "7_circuit"; string numQubits; string numParams; string fmGates;
                      string aGates ] :: csvRows

    | Error err -> pr "Error building ansatz: %s" err.Message

// ---------------------------------------------------------------------------
// Example 8 — Summary & recommendations
// ---------------------------------------------------------------------------
if shouldRun 8 then
    separator ()
    pr "EXAMPLE 8: Summary & recommendations"
    separator ()
    pr "VQC pipeline:"
    pr "  1. Feature encoding  — classical data -> quantum states"
    pr "  2. Parameterised circuit — trainable quantum transformations"
    pr "  3. Quantum gradients — parameter shift rule"
    pr "  4. Binary classification — standard ML task"
    pr ""
    pr "When to use VQC:"
    pr "  - High-dimensional feature spaces"
    pr "  - Non-linear decision boundaries"
    pr "  - Small-to-medium datasets"
    pr ""
    pr "Scale to real hardware:"
    pr "  let quantumBackend = IonQBackend(workspace, \"ionq.simulator\") :> IQuantumBackend"
    pr "  let quantumBackend = RigettiBackend(workspace, \"Aspen-M-3\") :> IQuantumBackend"

// ---------------------------------------------------------------------------
// Output
// ---------------------------------------------------------------------------
if outputPath.IsSome then
    let payload =
        {| script    = "VQCExample.fsx"
           backend   = "Local Simulator"
           timestamp = DateTime.UtcNow.ToString("o")
           epochs    = cliEpochs
           learningRate = cliLR
           shots     = cliShots
           ansatzDepth = cliDepth
           example   = exChoice
           results   = jsonResults |> List.rev |> List.map (fun (k,v) -> {| key = k; value = v |}) |}
    Reporting.writeJson outputPath.Value payload

if csvPath.IsSome then
    let header = [ "example"; "metric1"; "metric2"; "metric3"; "metric4" ]
    Reporting.writeCsv csvPath.Value header (csvRows |> List.rev)

// ---------------------------------------------------------------------------
// Usage hints
// ---------------------------------------------------------------------------
if not quiet && argv.Length = 0 then
    pr ""
    pr "Usage hints:"
    pr "  dotnet fsi VQCExample.fsx -- --example 3"
    pr "  dotnet fsi VQCExample.fsx -- --epochs 10 --learning-rate 0.05"
    pr "  dotnet fsi VQCExample.fsx -- --quiet --output r.json --csv r.csv"
    pr "  dotnet fsi VQCExample.fsx -- --help"
