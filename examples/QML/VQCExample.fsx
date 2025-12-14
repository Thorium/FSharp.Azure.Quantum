(*
    Variational Quantum Classifier (VQC) Example
    ============================================
    
    Demonstrates end-to-end quantum machine learning using VQC:
    - Binary classification with quantum circuits
    - Training with parameter shift rule
    - Model evaluation with standard ML metrics
    - Real quantum simulation with LocalBackend
    
    Run with: dotnet fsi VQCExample.fsx
*)

//#r "nuget: FSharp.Azure.Quantum"
#r "../../src/FSharp.Azure.Quantum/bin/Debug/net10.0/FSharp.Azure.Quantum.dll"

#r "nuget: FsUnit"
//#load "../../src/FSharp.Azure.Quantum/Types.fs"
//#load "../../src/FSharp.Azure.Quantum/Backends.fs"
//#load "../../src/FSharp.Azure.Quantum/LocalBackend.fs"
//#load "../../src/FSharp.Azure.Quantum/MachineLearning/QMLTypes.fs"
//#load "../../src/FSharp.Azure.Quantum/MachineLearning/FeatureMap.fs"
//#load "../../src/FSharp.Azure.Quantum/MachineLearning/VariationalForm.fs"
//#load "../../src/FSharp.Azure.Quantum/MachineLearning/VQC.fs"

open System
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.MachineLearning

// Helper function to print section headers
let printSection title =
    printfn ""
    printfn "%s" (String.replicate 60 "=")
    printfn "%s" title
    printfn "%s" (String.replicate 60 "=")
    printfn ""

// Helper function to print results
let printResult label value =
    printfn "%-30s: %s" label value

// Helper function to format float
let fmt (x: float) = sprintf "%.4f" x

// Helper function to format array
let fmtArray (xs: float array) = 
    xs |> Array.map fmt |> String.concat ", " |> sprintf "[%s]"

printSection "Variational Quantum Classifier (VQC) Example"

// ============================================================================
// 1. Setup: Backend and Architecture
// ============================================================================

printSection "1. Setup: Backend and Architecture"

// Create quantum backend
open FSharp.Azure.Quantum.Backends.LocalBackend
let backend = LocalBackend() :> FSharp.Azure.Quantum.Core.BackendAbstraction.IQuantumBackend
printResult "Backend" "LocalBackend (quantum simulator)"

// Define VQC architecture
let featureMap = AngleEncoding
printResult "Feature Map" "AngleEncoding (Ry rotations)"

let variationalForm = RealAmplitudes 2  // depth = 2
printResult "Variational Form" "RealAmplitudes (depth=2)"

// Training configuration
let config : VQC.TrainingConfig = {
    LearningRate = 0.1
    MaxEpochs = 5
    ConvergenceThreshold = 0.001
    Shots = 1000
    Verbose = false
    Optimizer = VQC.Adam { LearningRate = 0.1; Beta1 = 0.9; Beta2 = 0.999; Epsilon = 1e-8 }
    ProgressReporter = None
}

printfn ""
printResult "Learning Rate" (fmt config.LearningRate)
printResult "Max Epochs" (string config.MaxEpochs)
printResult "Convergence Tolerance" (fmt config.ConvergenceThreshold)
printResult "Shots per Circuit" (string config.Shots)

// ============================================================================
// 2. Dataset: Binary Classification (XOR-like Problem)
// ============================================================================

printSection "2. Dataset: Binary Classification"

// Training data: Simple 2D binary classification
// Class 0: Points near (0, 0) and (1, 1)
// Class 1: Points near (0, 1) and (1, 0)
let trainData = [|
    // Class 0 (bottom-left and top-right quadrants)
    [| 0.1; 0.1 |]; [| 0.2; 0.1 |]; [| 0.1; 0.2 |]
    [| 0.9; 0.9 |]; [| 0.8; 0.9 |]; [| 0.9; 0.8 |]
    
    // Class 1 (top-left and bottom-right quadrants)
    [| 0.1; 0.9 |]; [| 0.2; 0.8 |]; [| 0.1; 0.8 |]
    [| 0.9; 0.1 |]; [| 0.8; 0.2 |]; [| 0.9; 0.2 |]
|]

let trainLabels = [|
    0; 0; 0;  // Class 0
    0; 0; 0;
    1; 1; 1;  // Class 1
    1; 1; 1
|]

printResult "Training samples" (string trainData.Length)
printResult "Features per sample" (string trainData.[0].Length)
printResult "Class 0 samples" (trainLabels |> Array.filter ((=) 0) |> Array.length |> string)
printResult "Class 1 samples" (trainLabels |> Array.filter ((=) 1) |> Array.length |> string)

printfn ""
printfn "Sample data points:"
printfn "  Class 0: %s → %d" (fmtArray trainData.[0]) trainLabels.[0]
printfn "  Class 0: %s → %d" (fmtArray trainData.[5]) trainLabels.[5]
printfn "  Class 1: %s → %d" (fmtArray trainData.[6]) trainLabels.[6]
printfn "  Class 1: %s → %d" (fmtArray trainData.[11]) trainLabels.[11]

// Test data: Hold-out samples for evaluation
let testData = [|
    [| 0.15; 0.15 |]  // Class 0 (near bottom-left)
    [| 0.85; 0.85 |]  // Class 0 (near top-right)
    [| 0.15; 0.85 |]  // Class 1 (near top-left)
    [| 0.85; 0.15 |]  // Class 1 (near bottom-right)
|]

let testLabels = [| 0; 0; 1; 1 |]

printfn ""
printResult "Test samples" (string testData.Length)

// ============================================================================
// 3. Training: Quantum Circuit Optimization
// ============================================================================

printSection "3. Training: Quantum Circuit Optimization"

printfn "Training VQC with parameter shift rule..."

// Initialize parameters (small random values)
let numQubits = trainData.[0].Length
let initialParams = Array.init (numQubits * 2) (fun _ -> 0.1)  // Simplified initialization

let trainResult = VQC.train backend featureMap variationalForm initialParams trainData trainLabels config

match trainResult with
| Error err ->
    printfn "❌ Training failed: %s" err.Message
    
| Ok result ->
    printfn "✅ Training completed successfully"
    printfn ""
    
    // Training metrics
    printResult "Final Parameters" (fmtArray result.Parameters)
    printResult "Training Accuracy" (fmt result.TrainAccuracy)
    printResult "Epochs Run" (string result.Epochs)
    printResult "Converged" (if result.Converged then "✓ Yes" else "✗ No (reached max epochs)")
    
    printfn ""
    printfn "Loss History (first 10 epochs):"
    result.LossHistory 
    |> List.take (min 10 result.LossHistory.Length)
    |> List.iteri (fun i loss -> printfn "  Epoch %2d: %s" (i+1) (fmt loss))
    
    if result.LossHistory.Length > 10 then
        printfn "  ..."
        printfn "  Epoch %2d: %s" 
            result.LossHistory.Length 
            (fmt (List.last result.LossHistory))
    
    // ============================================================================
    // 4. Prediction: Individual Sample Classification
    // ============================================================================
    
    printSection "4. Prediction: Individual Sample Classification"
    
    printfn "Making predictions on test samples..."
    printfn ""
    
    testData
    |> Array.iteri (fun i sample ->
        let predResult = VQC.predict backend featureMap variationalForm result.Parameters sample config.Shots
        match predResult with
        | Ok pred ->
            let correct = if pred.Label = testLabels.[i] then "✓" else "✗"
            printfn "Sample %d: %s" (i+1) (fmtArray sample)
            printfn "  Prediction: Class %d (probability: %s)" pred.Label (fmt pred.Probability)
            printfn "  True Label: Class %d" testLabels.[i]
            printfn "  Correct:    %s" correct
            printfn ""
        | Error err ->
            printfn "Sample %d: Prediction failed - %s" (i+1) err.Message
    )
    
    // ============================================================================
    // 5. Evaluation: Model Performance Metrics
    // ============================================================================
    
    printSection "5. Evaluation: Model Performance Metrics"
    
    // Evaluate on training set
    printfn "Training Set Evaluation:"
    printfn ""
    
    let trainEval = VQC.evaluate backend featureMap variationalForm result.Parameters trainData trainLabels config.Shots
    match trainEval with
    | Ok accuracy ->
        printResult "Accuracy" (sprintf "%s (%.1f%%)" (fmt accuracy) (accuracy * 100.0))
    | Error err ->
        printfn "❌ Evaluation failed: %s" err.Message
    
    printfn ""
    
    // Evaluate on test set
    printfn "Test Set Evaluation:"
    printfn ""
    
    let testEval = VQC.evaluate backend featureMap variationalForm result.Parameters testData testLabels config.Shots
    match testEval with
    | Ok accuracy ->
        printResult "Accuracy" (sprintf "%s (%.1f%%)" (fmt accuracy) (accuracy * 100.0))
    | Error err ->
        printfn "❌ Evaluation failed: %s" err.Message
    
    // ============================================================================
    // 6. Confusion Matrix: Detailed Classification Analysis
    // ============================================================================
    
    printSection "6. Confusion Matrix: Detailed Analysis"
    
    let confMatrix = VQC.confusionMatrix backend featureMap variationalForm result.Parameters testData testLabels config.Shots
    
    match confMatrix with
    | Ok cm ->
        printfn "Confusion Matrix (Test Set):"
        printfn ""
        printfn "                    Predicted"
        printfn "                Class 0  Class 1"
        printfn "Actual  Class 0    %2d       %2d" cm.TrueNegatives cm.FalsePositives
        printfn "        Class 1    %2d       %2d" cm.FalseNegatives cm.TruePositives
        printfn ""
        printResult "True Positives (TP)" (string cm.TruePositives)
        printResult "True Negatives (TN)" (string cm.TrueNegatives)
        printResult "False Positives (FP)" (string cm.FalsePositives)
        printResult "False Negatives (FN)" (string cm.FalseNegatives)
        printfn ""
        
        // Derived metrics using VQC helper functions
        let precision = VQC.precision cm
        let recall = VQC.recall cm
        let f1 = VQC.f1Score cm
        let accuracy = float (cm.TruePositives + cm.TrueNegatives) / float testData.Length
        
        printfn "Derived Metrics:"
        printResult "  Accuracy" (sprintf "%s (%.1f%%)" (fmt accuracy) (accuracy * 100.0))
        printResult "  Precision" (fmt precision)
        printResult "  Recall" (fmt recall)
        printResult "  F1 Score" (fmt f1)
        
    | Error err ->
        printfn "❌ Confusion matrix failed: %s" err.Message

// ============================================================================
// 7. Quantum Circuit Analysis
// ============================================================================

printSection "7. Quantum Circuit Analysis"

let numParams = AnsatzHelpers.parameterCount variationalForm numQubits

printResult "Number of Qubits" (string numQubits)
printResult "Number of Parameters" (string numParams)

// Estimate circuit complexity  
let featureMapCircuit = FeatureMap.angleEncoding trainData.[0]
printResult "Feature Map Gates" (string featureMapCircuit.Gates.Length)

let ansatzCircuit = VariationalForms.buildVariationalForm variationalForm (Array.create numParams 0.0) numQubits
match ansatzCircuit with
| Ok aCircuit ->
    printResult "Variational Form Gates" (string aCircuit.Gates.Length)
    printResult "Total Circuit Gates" (string (featureMapCircuit.Gates.Length + aCircuit.Gates.Length))
    
    // Gradient computation cost
    let gradientsPerEpoch = numParams * 2  // Parameter shift rule requires 2 evaluations per parameter
    let circuitsPerSample = 1  // Forward pass
    let totalCircuitsPerEpoch = trainData.Length * circuitsPerSample + gradientsPerEpoch * trainData.Length
    
    printfn ""
    printfn "Training Complexity:"
    printResult "  Circuits per Sample" (string circuitsPerSample)
    printResult "  Gradient Evals per Param" "2 (parameter shift rule)"
    printResult "  Total Circuits per Epoch" (string totalCircuitsPerEpoch)
    
    match trainResult with
    | Ok result ->
        let totalCircuits = totalCircuitsPerEpoch * result.Epochs
        printResult "  Total Circuits (Training)" (string totalCircuits)
    | _ -> ()
    
| Error err ->
    printfn "Error creating variational form: %s" err.Message

// ============================================================================
// 8. Summary and Recommendations
// ============================================================================

printSection "8. Summary and Recommendations"

printfn "Quantum Machine Learning with VQC:"
printfn ""
printfn "✅ Feature Encoding: Classical data → Quantum states"
printfn "✅ Parameterized Circuits: Trainable quantum transformations"
printfn "✅ Quantum Gradients: Parameter shift rule for optimization"
printfn "✅ Binary Classification: Standard ML task with quantum advantage"
printfn ""

printfn "When to Use VQC:"
printfn ""
printfn "  ✓ High-dimensional feature spaces"
printfn "  ✓ Non-linear decision boundaries"
printfn "  ✓ Small to medium datasets"
printfn "  ✓ Quantum hardware available"
printfn ""

printfn "VQC Advantages:"
printfn ""
printfn "  • Quantum feature spaces (exponentially large)"
printfn "  • Entanglement captures complex patterns"
printfn "  • Proven advantages for certain problems"
printfn "  • Works on NISQ devices"
printfn ""

printfn "Next Steps:"
printfn ""
printfn "  1. Try different feature maps (ZZ, Pauli)"
printfn "  2. Experiment with variational forms (TwoLocal, EfficientSU2)"
printfn "  3. Tune hyperparameters (learning rate, depth)"
printfn "  4. Scale to larger datasets"
printfn "  5. Deploy on real quantum hardware (IonQ, Rigetti)"
printfn ""

printfn "Real Quantum Hardware:"
printfn ""
printfn "  // Replace LocalBackend with Azure Quantum:"
printfn "  // let backend = IonQBackend(workspace, \"ionq.simulator\") :> IQuantumBackend"
printfn "  // let backend = RigettiBackend(workspace, \"Aspen-M-3\") :> IQuantumBackend"
printfn ""

printSection "VQC Example Complete!"

printfn "This example demonstrated:"
printfn "  ✓ Binary classification with quantum circuits"
printfn "  ✓ Training with parameter shift rule"
printfn "  ✓ Model evaluation with ML metrics"
printfn "  ✓ Real quantum simulation"
printfn ""
printfn "The VQC framework is production-ready for quantum machine learning!"
printfn ""
