namespace FSharp.Azure.Quantum.MachineLearning

open FSharp.Azure.Quantum.Core

/// Types for Quantum Machine Learning (QML)
///
/// This module provides core types for quantum machine learning:
/// - Feature maps: Encode classical data into quantum states
/// - Training data: Labeled datasets for supervised learning
/// - Model parameters: Variational circuit parameters
/// - Predictions: Classification/regression outputs

open System

/// Classical feature vector (input data)
type FeatureVector = float array

/// Class label for classification
type Label = int

/// Training example with features and label
type TrainingExample = {
    Features: FeatureVector
    Label: Label
}

/// Training dataset
type TrainingDataset = TrainingExample array

/// Feature map strategy for encoding classical data
[<Struct>]
type FeatureMapType =
    /// ZZ feature map: exp(-i * φ(x) * Z_i ⊗ Z_j)
    | ZZFeatureMap of depth: int
    
    /// Pauli feature map: exp(-i * φ(x) * P) for Pauli operators P
    | PauliFeatureMap of paulis: string list * depth: int
    
    /// Angle encoding: Ry(x_i) on each qubit
    | AngleEncoding
    
    /// Amplitude encoding: |ψ⟩ = ∑ x_i |i⟩ (normalized)
    | AmplitudeEncoding

/// Variational form (ansatz) for parameterized circuits
[<Struct>]
type VariationalForm =
    /// Real Amplitudes: Ry rotations + CZ entanglement
    | RealAmplitudes of depth: int
    
    /// Two-local: Single-qubit rotations + two-qubit entanglement
    | TwoLocal of rotation: string * entanglement: string * depth: int
    
    /// Efficient SU(2): Hardware-efficient ansatz
    | EfficientSU2 of depth: int

/// QML model parameters
type ModelParameters = {
    /// Feature map configuration
    FeatureMap: FeatureMapType
    
    /// Variational form (ansatz)
    Ansatz: VariationalForm
    
    /// Trainable parameters (rotation angles)
    Weights: float array
    
    /// Number of qubits
    NumQubits: int
}

/// Prediction result from QML model
type Prediction = {
    /// Predicted class label
    PredictedLabel: Label
    
    /// Prediction confidence/probability
    Confidence: float
    
    /// Class probabilities for all labels
    ClassProbabilities: Map<Label, float>
}

/// Training configuration
type TrainingConfig = {
    /// Maximum training iterations
    MaxIterations: int
    
    /// Learning rate for parameter updates
    LearningRate: float
    
    /// Convergence tolerance
    Tolerance: float
    
    /// Batch size for mini-batch training
    BatchSize: int option
    
    /// Random seed for reproducibility
    Seed: int option
}

/// Training metrics
type TrainingMetrics = {
    /// Training accuracy per iteration
    TrainingAccuracy: float array
    
    /// Validation accuracy per iteration (if validation set provided)
    ValidationAccuracy: float array option
    
    /// Loss value per iteration
    Loss: float array
    
    /// Number of iterations performed
    Iterations: int
    
    /// Final model parameters
    FinalParameters: float array
}

/// Quantum kernel evaluation
type QuantumKernel = {
    /// Kernel function: K(x, y) using quantum circuit overlap
    Evaluate: FeatureVector -> FeatureVector -> float
    
    /// Feature map used for kernel
    FeatureMap: FeatureMapType
    
    /// Number of qubits
    NumQubits: int
}

/// Kernel matrix for dataset
type KernelMatrix = float[,]

/// Module for feature map utilities
module FeatureMapHelpers =
    
    /// Calculate number of parameters needed for feature map
    let parameterCount (featureMap: FeatureMapType) (numQubits: int) : int =
        match featureMap with
        | ZZFeatureMap depth ->
            // Each layer: 1 parameter per qubit + entanglement
            numQubits * depth
        | PauliFeatureMap(paulis, depth) ->
            // Parameters for each Pauli term per layer
            paulis.Length * depth
        | AngleEncoding ->
            // One parameter per qubit (no depth)
            numQubits
        | AmplitudeEncoding ->
            // Amplitude encoding uses all features directly
            // Number of features must be 2^n
            0  // No trainable parameters
    
    /// Validate feature vector dimensions
    let validateFeatures (numQubits: int) (features: FeatureVector) (featureMap: FeatureMapType) : QuantumResult<unit> =
        match featureMap with
        | AmplitudeEncoding ->
            let expected = 1 <<< numQubits  // 2^numQubits
            if features.Length <> expected then
                Error (QuantumError.ValidationError ("Input", $"AmplitudeEncoding requires {expected} features for {numQubits} qubits, got {features.Length}"))
            else
                Ok ()
        | AngleEncoding ->
            if features.Length <> numQubits then
                Error (QuantumError.ValidationError ("Input", $"AngleEncoding requires {numQubits} features for {numQubits} qubits, got {features.Length}"))
            else
                Ok ()
        | ZZFeatureMap _ | PauliFeatureMap _ ->
            // These can accept variable-length features (repeated/truncated as needed)
            Ok ()
    
    /// Normalize feature vector for amplitude encoding
    let normalizeForAmplitude (features: FeatureVector) : FeatureVector =
        let norm = features |> Array.sumBy (fun x -> x * x) |> sqrt
        if norm < 1e-10 then
            // Avoid division by zero
            Array.create features.Length (1.0 / sqrt (float features.Length))
        else
            features |> Array.map (fun x -> x / norm)

/// Module for ansatz (variational form) utilities
module AnsatzHelpers =
    
    /// Calculate number of trainable parameters for ansatz
    let parameterCount (ansatz: VariationalForm) (numQubits: int) : int =
        match ansatz with
        | RealAmplitudes depth ->
            // Each layer: 1 Ry rotation per qubit
            numQubits * depth
        
        | TwoLocal(_, _, depth) ->
            // Each layer: 1 rotation per qubit
            numQubits * depth
        
        | EfficientSU2 depth ->
            // Each layer: 2 rotations per qubit (Ry, Rz)
            2 * numQubits * depth
    
    /// Generate random initial parameters
    let randomInitialParameters (ansatz: VariationalForm) (numQubits: int) (seed: int option) : float array =
        let rng = 
            match seed with
            | Some s -> Random(s)
            | None -> Random()
        
        let count = parameterCount ansatz numQubits
        Array.init count (fun _ -> 2.0 * Math.PI * rng.NextDouble())
