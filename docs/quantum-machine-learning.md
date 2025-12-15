---
layout: default
title: Quantum Machine Learning
---

# Quantum Machine Learning (QML)

**Apply quantum computing to machine learning problems** using variational quantum circuits, quantum kernels, and quantum feature spaces.

## Overview

Quantum Machine Learning (QML) leverages quantum computing to enhance classical machine learning algorithms. The FSharp.Azure.Quantum library provides production-ready implementations of key QML algorithms:

- **Variational Quantum Classifier (VQC)** - Supervised learning with parameterized quantum circuits
- **Quantum Kernel SVM** - Support vector machines using quantum feature spaces
- **Feature Maps** - Encode classical data into quantum states
- **Variational Forms** - Parameterized ansatz circuits for training
- **Optimizers** - Adam, SGD for quantum circuit parameter training

## Key Concepts

### Quantum vs Classical Machine Learning

**Classical ML:**
- Features → Model (weights) → Predictions
- Training adjusts weights to minimize loss
- Limited to classical feature spaces

**Quantum ML:**
- Features → Quantum Feature Map → Quantum State
- Variational Circuit (trainable parameters) → Measurement
- Access to exponentially large quantum feature spaces
- Potential quantum advantage for certain datasets

### When to Use QML

**Use QML When:**
- Feature space complexity benefits from quantum encoding
- Pattern recognition requires high-dimensional representations
- Exploring quantum advantage in machine learning
- Research and development of quantum algorithms

**Use Classical ML When:**
- Dataset is simple or low-dimensional
- Training time is critical (QML has overhead)
- Production deployment requires classical infrastructure
- Interpretability is paramount

## Variational Quantum Classifier (VQC)

### What is VQC?

VQC is a supervised learning algorithm that uses parameterized quantum circuits to classify data:

1. **Feature Encoding**: Classical data → Quantum state (via Feature Map)
2. **Variational Circuit**: Apply parameterized gates (trainable)
3. **Measurement**: Quantum state → Classical prediction
4. **Training**: Optimize parameters to minimize classification loss

### API Reference

```fsharp
open FSharp.Azure.Quantum.MachineLearning

// Training configuration
let config : VQC.TrainingConfig = {
    LearningRate = 0.1
    MaxEpochs = 50
    ConvergenceThreshold = 0.001
    Shots = 1000
    Verbose = true
    Optimizer = VQC.Adam { 
        LearningRate = 0.1
        Beta1 = 0.9
        Beta2 = 0.999
        Epsilon = 1e-8 
    }
    ProgressReporter = None
}

// Define architecture
let featureMap = AngleEncoding           // Feature encoding strategy
let variationalForm = RealAmplitudes 2   // Ansatz with depth=2

// Train classifier
match VQC.train backend featureMap variationalForm initialParams trainFeatures trainLabels config with
| Ok trainedModel ->
    printfn "Training complete!"
    printfn "Final loss: %.4f" trainedModel.FinalLoss
    printfn "Accuracy: %.2f%%" (trainedModel.Metrics.Accuracy * 100.0)
    
    // Make predictions
    match VQC.predict backend featureMap variationalForm trainedModel.Parameters testPoint 1000 with
    | Ok prediction ->
        printfn "Predicted class: %d (confidence: %.2f%%)" prediction.Label (prediction.Confidence * 100.0)
    | Error err -> eprintfn "Prediction error: %s" err.Message
    
| Error err -> eprintfn "Training error: %s" err.Message
```

### VQC Training Process

**Hybrid Quantum-Classical Loop:**

1. **Initialize**: Random circuit parameters θ
2. **Forward Pass**:
   - Encode training sample → |ψ(x)⟩
   - Apply variational circuit U(θ) → |ψ(x,θ)⟩
   - Measure expectation value → prediction
3. **Compute Loss**: Compare prediction to label
4. **Gradient Estimation**: Parameter shift rule (quantum gradients)
5. **Update Parameters**: θ ← θ - η∇L (via Adam/SGD)
6. **Repeat** until convergence or max epochs

### Available Optimizers

**Adam (Adaptive Moment Estimation)** - Recommended
```fsharp
Optimizer = VQC.Adam {
    LearningRate = 0.1
    Beta1 = 0.9          // Momentum decay rate
    Beta2 = 0.999        // Variance decay rate
    Epsilon = 1e-8       // Numerical stability
}
```

**SGD (Stochastic Gradient Descent)** - Simple baseline
```fsharp
Optimizer = VQC.SGD
LearningRate = 0.01
```

### Model Serialization

Save and load trained models:

```fsharp
// Save trained model
VQC.saveModel trainedModel "fraud_classifier.model"

// Load model for inference
match VQC.loadModel "fraud_classifier.model" with
| Ok model ->
    let predictions = testData |> Array.map (fun x -> 
        VQC.predict backend featureMap variationalForm model.Parameters x 1000
    )
| Error err -> eprintfn "Load error: %s" err.Message
```

## Quantum Kernel SVM

### What is Quantum Kernel SVM?

Quantum kernels leverage quantum feature spaces to compute similarity between data points:

**Classical Kernel SVM:**
- Kernel K(x, y) = ⟨φ(x), φ(y)⟩ (classical feature space)
- Limited to polynomial, RBF, etc.

**Quantum Kernel SVM:**
- Kernel K(x, y) = |⟨ψ(x)|ψ(y)⟩|² (quantum feature space)
- Access to exponentially large Hilbert space
- Quantum feature map determines kernel properties

### API Reference

```fsharp
open FSharp.Azure.Quantum.MachineLearning

// Setup quantum feature map
let featureMap = ZZFeatureMap 2  // Depth-2 entangling feature map

// Training configuration
let config : QuantumKernelSVM.TrainingConfig = {
    C = 1.0                      // Regularization parameter
    Kernel = QuantumKernel       // Use quantum kernel
    MaxIterations = 1000
    Tolerance = 0.001
    Verbose = true
}

// Train SVM with quantum kernel
match QuantumKernelSVM.train backend featureMap trainData trainLabels config 1000 with
| Ok model ->
    printfn "SVM trained successfully"
    printfn "Support vectors: %d" model.SupportVectors.Length
    
    // Evaluate on test set
    match QuantumKernelSVM.evaluate backend model testData testLabels 1000 with
    | Ok metrics ->
        printfn "Test Accuracy: %.2f%%" (metrics.Accuracy * 100.0)
        printfn "Precision: %.2f%%" (metrics.Precision * 100.0)
        printfn "Recall: %.2f%%" (metrics.Recall * 100.0)
    | Error err -> eprintfn "Evaluation error: %s" err.Message
    
| Error err -> eprintfn "Training error: %s" err.Message
```

### How Quantum Kernels Work

**Kernel Computation:**
1. Encode x and y into quantum states: |ψ(x)⟩ and |ψ(y)⟩
2. Prepare state |ψ(x)⟩, then apply inverse of |ψ(y)⟩
3. Measure overlap: K(x, y) = |⟨0|U†(y)U(x)|0⟩|²
4. Result is quantum-enhanced similarity metric

**Kernel Matrix:**
- For N training samples, compute N×N kernel matrix
- Each entry requires quantum circuit execution
- Matrix is symmetric and positive semi-definite

## Feature Maps

### What are Feature Maps?

Feature maps encode classical data into quantum states. The choice of feature map determines:
- Quantum state representation
- Entanglement structure
- Expressiveness of quantum model

### Available Feature Maps

#### 1. Angle Encoding

**Strategy:** Encode each feature as rotation angle

```fsharp
let featureMap = AngleEncoding

// Circuit: Ry(π * x_i) on each qubit i
// - Simple, no entanglement
// - One qubit per feature
// - Good baseline for testing
```

**Use When:**
- Quick prototyping
- Low-dimensional data (≤20 features)
- Interpretability is important

#### 2. ZZ Feature Map

**Strategy:** Hadamard + Rz rotations + ZZ entanglement

```fsharp
let featureMap = ZZFeatureMap 2  // depth = 2 layers

// Circuit structure (per layer):
// 1. H on all qubits (superposition)
// 2. Rz(2π * x_i) on each qubit
// 3. CNOT + Rz(2π * x_i * x_j) for pairs (entanglement)
// 4. Repeat for depth layers

// - High entanglement
// - Captures feature correlations
// - Recommended for most tasks
```

**Use When:**
- General-purpose classification
- Feature correlations matter
- Need expressive quantum states

#### 3. Pauli Feature Map

**Strategy:** Custom Pauli string rotations

```fsharp
let pauliStrings = [
    [| X; X; I |]  // XX rotation on qubits 0,1
    [| Z; Z; I |]  // ZZ rotation on qubits 0,1
    [| Y; I; Y |]  // YY rotation on qubits 0,2
]
let featureMap = PauliFeatureMap pauliStrings

// - Maximum flexibility
// - Domain-specific encodings
// - Advanced use cases
```

**Use When:**
- Custom problem structure
- Specific symmetries to exploit
- Research and experimentation

### Feature Map Comparison

| Feature Map | Qubits | Entanglement | Depth | Best For |
|-------------|--------|--------------|-------|----------|
| **AngleEncoding** | n (one per feature) | None | 1 | Baselines, small data |
| **ZZFeatureMap** | n | High (pairwise) | Configurable | General classification |
| **PauliFeatureMap** | n | Custom | Custom | Domain-specific tasks |

## Variational Forms (Ansatz Circuits)

### What are Variational Forms?

Variational forms are parameterized quantum circuits used in VQC. They define:
- Gate structure (which gates, which qubits)
- Trainable parameters (rotation angles)
- Expressiveness of the model

### Available Variational Forms

#### 1. RealAmplitudes

**Structure:** Ry rotations + CNOT entanglement

```fsharp
let variationalForm = RealAmplitudes 3  // depth = 3 layers

// Circuit structure (per layer):
// - Ry(θ_i) on each qubit
// - CNOT ladder (linear entanglement)
// - Total parameters = n_qubits * depth

// - Simple, efficient
// - Good for many tasks
// - Recommended default
```

**Use When:**
- Starting point for VQC
- Limited quantum resources
- Fast training required

#### 2. EfficientSU2

**Structure:** Ry + Rz rotations + CNOT entanglement

```fsharp
let variationalForm = EfficientSU2 2  // depth = 2 layers

// Circuit structure (per layer):
// - Ry(θ_i) on each qubit
// - Rz(φ_i) on each qubit
// - CNOT circular entanglement
// - Total parameters = 2 * n_qubits * depth

// - More expressive than RealAmplitudes
// - Full SU(2) rotations
// - Better approximation capability
```

**Use When:**
- Complex classification tasks
- RealAmplitudes underfits
- More parameters affordable

### Variational Form Comparison

| Variational Form | Rotations | Entanglement | Parameters | Expressiveness |
|------------------|-----------|--------------|------------|----------------|
| **RealAmplitudes** | Ry | Linear | n×d | Medium |
| **EfficientSU2** | Ry, Rz | Circular | 2n×d | High |

## Complete Example: Binary Classification

```fsharp
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.MachineLearning
open FSharp.Azure.Quantum.Backends.LocalBackend

// Setup backend
let backend = LocalBackend() :> IQuantumBackend

// Prepare dataset (XOR-like problem)
let trainData = [|
    [| 0.1; 0.1 |]; [| 0.2; 0.1 |]; [| 0.9; 0.9 |]; [| 0.8; 0.9 |]  // Class 0
    [| 0.1; 0.9 |]; [| 0.2; 0.8 |]; [| 0.9; 0.1 |]; [| 0.8; 0.2 |]  // Class 1
|]
let trainLabels = [| 0; 0; 0; 0; 1; 1; 1; 1 |]

// Define QML architecture
let featureMap = ZZFeatureMap 2
let variationalForm = RealAmplitudes 2

// Training configuration
let config = {
    LearningRate = 0.1
    MaxEpochs = 50
    ConvergenceThreshold = 0.001
    Shots = 1000
    Verbose = true
    Optimizer = VQC.Adam { 
        LearningRate = 0.1
        Beta1 = 0.9
        Beta2 = 0.999
        Epsilon = 1e-8 
    }
    ProgressReporter = None
}

// Initialize parameters (random)
let initialParams = VQC.initializeParameters variationalForm trainData.[0].Length

// Train VQC
match VQC.train backend featureMap variationalForm initialParams trainData trainLabels config with
| Ok model ->
    printfn "Training complete!"
    printfn "Accuracy: %.2f%%" (model.Metrics.Accuracy * 100.0)
    
    // Test on new data
    let testPoint = [| 0.15; 0.85 |]  // Should be class 1
    match VQC.predict backend featureMap variationalForm model.Parameters testPoint 1000 with
    | Ok prediction ->
        printfn "Prediction: Class %d (%.2f%% confidence)" 
            prediction.Label 
            (prediction.Confidence * 100.0)
    | Error err -> eprintfn "Error: %s" err.Message
    
| Error err -> eprintfn "Training failed: %s" err.Message
```

## Performance Considerations

### Training Time

**Factors affecting training time:**
- **Number of parameters**: More parameters → longer training
- **Dataset size**: More samples → longer epoch
- **Shots**: More shots → better gradient estimates but slower
- **Optimizer**: Adam typically converges faster than SGD
- **Depth**: Deeper circuits → more gate operations

**Typical Training Times (LocalBackend):**
- Small dataset (10 samples, 2 qubits): ~30 seconds
- Medium dataset (100 samples, 4 qubits): ~5 minutes
- Large dataset (1000 samples, 8 qubits): ~30 minutes

### Hyperparameter Tuning

**Key hyperparameters to tune:**

1. **Learning Rate** (0.001 - 0.5)
   - Too high: Training unstable, oscillation
   - Too low: Slow convergence, local minima
   - Start with 0.1, reduce if unstable

2. **Depth** (1-5 layers)
   - Too shallow: Underfitting, poor accuracy
   - Too deep: Overfitting, slow training
   - Start with 2, increase if underfitting

3. **Shots** (100-10000)
   - Too few: Noisy gradients, poor convergence
   - Too many: Slow training, diminishing returns
   - Use 100 for optimization, 1000 for final evaluation

4. **Feature Map**
   - AngleEncoding: Fast, simple, baseline
   - ZZFeatureMap: Better for most tasks
   - PauliFeatureMap: Domain-specific

## Troubleshooting

### Common Issues

#### 1. Training Loss Not Decreasing

**Symptoms:** Loss remains constant or increases

**Solutions:**
- Reduce learning rate (try 0.01 instead of 0.1)
- Increase shots (gradient estimates too noisy)
- Try different optimizer (Adam vs SGD)
- Check data normalization (features should be [-1, 1] or [0, 1])

#### 2. Overfitting (High Train Accuracy, Low Test Accuracy)

**Symptoms:** Training accuracy > 95%, test accuracy < 60%

**Solutions:**
- Reduce model complexity (lower depth)
- Increase training data
- Add regularization (higher C in SVM)
- Use simpler feature map (AngleEncoding)

#### 3. Poor Accuracy on Both Train and Test

**Symptoms:** Accuracy ~50% (random guessing)

**Solutions:**
- Increase model expressiveness (higher depth)
- Use more expressive feature map (ZZFeatureMap)
- Increase max epochs
- Check data quality (labels correct?)

#### 4. "Too many qubits" Error

**Symptoms:** Backend error on circuit execution

**Solutions:**
- LocalBackend supports ≤20 qubits
- Reduce feature dimensionality (PCA, feature selection)
- Use cloud backend for larger circuits
- Batch features (train multiple smaller classifiers)

## Working Examples

See complete, runnable examples in the `examples/QML/` directory:

- **[VQCExample.fsx](https://github.com/Thorium/FSharp.Azure.Quantum/tree/main/examples/QML/VQCExample.fsx)** - End-to-end VQC training pipeline
- **[FeatureMapExample.fsx](https://github.com/Thorium/FSharp.Azure.Quantum/tree/main/examples/QML/FeatureMapExample.fsx)** - Feature encoding demonstrations
- **[VariationalFormExample.fsx](https://github.com/Thorium/FSharp.Azure.Quantum/tree/main/examples/QML/VariationalFormExample.fsx)** - Ansatz circuit examples

## See Also

- [Business Problem Builders](business-problem-builders) - High-level APIs using QML (AutoML, Fraud Detection)
- [Getting Started Guide](getting-started) - Installation and setup
- [API Reference](api-reference) - Complete API documentation
- [Local Simulation](local-simulation) - LocalBackend for QML development
- [Backend Switching](backend-switching) - Cloud quantum backends for larger problems

## References

- **VQC Algorithm**: [Benedetti et al., Quantum Science and Technology (2019)](https://arxiv.org/abs/1804.11326)
- **Quantum Kernels**: [Havlíček et al., Nature (2019)](https://arxiv.org/abs/1803.07128)
- **QML Survey**: [Biamonte et al., Nature (2017)](https://arxiv.org/abs/1611.09347)

---

**Last Updated**: December 2025
