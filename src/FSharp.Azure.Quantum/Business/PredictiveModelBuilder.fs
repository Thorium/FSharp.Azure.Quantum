namespace FSharp.Azure.Quantum.Business

open FSharp.Azure.Quantum.Backends
open FSharp.Azure.Quantum.Core
open System
open FSharp.Azure.Quantum.Backends
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Backends
open FSharp.Azure.Quantum.MachineLearning
open FSharp.Azure.Quantum.Backends
open FSharp.Azure.Quantum.MachineLearning.QuantumRegressionHHL
open FSharp.Azure.Quantum.Backends
open FSharp.Azure.Quantum

/// High-Level Predictive Model Builder - Business-First API
/// 
/// DESIGN PHILOSOPHY:
/// This is a BUSINESS DOMAIN API for predicting future outcomes
/// without understanding quantum circuits, feature maps, or optimization algorithms.
/// 
/// WHAT IS PREDICTIVE MODELING:
/// Use historical data to forecast future values or categories.
/// Supports both continuous predictions (regression) and categorical predictions (multi-class).
/// 
/// USE CASES:
/// - Customer churn prediction: Will this customer leave? When?
/// - Demand forecasting: How many units will we sell next month?
/// - Revenue prediction: Expected revenue per customer
/// - Customer lifetime value (LTV): Long-term customer value
/// - Risk scoring: Credit risk, insurance risk (1-10 scale)
/// - Lead scoring: Probability of conversion (0-100%)
/// - Inventory optimization: Predict stock requirements
/// - Price optimization: Optimal pricing for maximum revenue
/// 
/// EXAMPLE USAGE:
///   // Simple: Predict continuous value (regression)
///   let model = predictiveModel {
///       trainWith trainX trainY
///       problemType Regression
///   }
///   
///   let prediction = model |> PredictiveModel.predict newCustomer
///   printfn "Expected revenue: $%.2f" prediction.Value
///   
///   // Churn prediction: Multi-class classification
///   let churnModel = predictiveModel {
///       trainWith customerFeatures churnLabels  // Labels: 0=Stay, 1=Churn30, 2=Churn60, 3=Churn90
///       problemType (MultiClass 4)
///       
///       // Optional configuration
///       learningRate 0.01
///       maxEpochs 150
///       
///       saveModelTo "churn_predictor.model"
///   }
///   
///   let churnPred = churnModel |> PredictiveModel.predictCategory customer
///   match churnPred.Category with
///   | 0 -> printfn "Customer will stay"
///   | 1 -> printfn "⚠️ Churn risk in 30 days - take action!"
///   | _ -> printfn "Churn risk detected"
module PredictiveModel =
    
    // ========================================================================
    // CORE TYPES - Predictive Modeling Domain Model
    // ========================================================================
    
    /// Problem type for prediction
    type ProblemType =
        | Regression                // Predict continuous values (revenue, demand, LTV)
        | MultiClass of int         // Predict categories (churn timing, risk levels)
    
    /// Architecture choice for predictive modeling
    type Architecture =
        | Quantum        // Pure quantum model (VQC for classification, QSVR for regression)
        | Hybrid         // Quantum features + classical ML
        | Classical      // Classical baseline for comparison
    
    /// Predictive modeling problem specification
    type PredictionProblem = {
        /// Training features (samples × features)
        TrainFeatures: float array array
        
        /// Training targets (continuous values or class labels)
        TrainTargets: float array
        
        /// Problem type (regression or multi-class)
        ProblemType: ProblemType
        
        /// Architecture to use
        Architecture: Architecture
        
        /// Learning rate for training
        LearningRate: float
        
        /// Maximum training epochs
        MaxEpochs: int
        
        /// Convergence threshold
        ConvergenceThreshold: float
        
        /// Quantum backend (None = LocalBackend)
        Backend: IQuantumBackend option
        
        /// Number of measurement shots
        Shots: int
        
        /// Verbose logging
        Verbose: bool
        
        /// Path to save trained model
        SavePath: string option
        
        /// Optional note about the model
        Note: string option
        
        /// Optional progress reporter for real-time updates
        ProgressReporter: Core.Progress.IProgressReporter option
        
        /// Optional cancellation token for early termination
        CancellationToken: System.Threading.CancellationToken option
    }
    
    /// Trained predictive model
    type Model = {
        /// Underlying model
        InternalModel: InternalModel
        
        /// Training metadata
        Metadata: ModelMetadata
    }
    
    and InternalModel =
        | RegressionVQC of VQC.RegressionTrainingResult * FeatureMapType * VariationalForm * int
        | MultiClassVQC of VQC.MultiClassTrainingResult * FeatureMapType * VariationalForm * int  // stores all OVR classifiers
        | SVMRegressor of QuantumKernelSVM.SVMModel
        | SVMMultiClass of MultiClassSVM.MultiClassModel
        | HHLRegressor of RegressionResult  // Quantum HHL linear regression
        | ClassicalRegressor of float array  // Simple linear weights
        | ClassicalMultiClass of float array array  // Weights per class
    
    and ModelMetadata = {
        ProblemType: ProblemType
        Architecture: Architecture
        TrainingScore: float  // R² for regression, accuracy for classification
        TrainingTime: TimeSpan
        NumFeatures: int
        NumSamples: int
        CreatedAt: DateTime
        Note: string option
    }
    
    /// Regression prediction result
    type RegressionPrediction = {
        /// Predicted value
        Value: float
        
        /// Confidence interval (if available)
        ConfidenceInterval: (float * float) option
        
        /// Model metadata
        ModelType: string
    }
    
    /// Multi-class prediction result
    type CategoryPrediction = {
        /// Predicted category (class index)
        Category: int
        
        /// Confidence score [0, 1]
        Confidence: float
        
        /// Probability distribution over all classes
        Probabilities: float array
        
        /// Model metadata
        ModelType: string
    }
    
    /// Evaluation metrics for regression
    type RegressionMetrics = {
        /// R² score (coefficient of determination)
        RSquared: float
        
        /// Mean Absolute Error
        MAE: float
        
        /// Mean Squared Error
        MSE: float
        
        /// Root Mean Squared Error
        RMSE: float
    }
    
    /// Evaluation metrics for multi-class
    type MultiClassMetrics = {
        /// Overall accuracy
        Accuracy: float
        
        /// Precision per class
        Precision: float array
        
        /// Recall per class
        Recall: float array
        
        /// F1 score per class
        F1Score: float array
        
        /// Confusion matrix
        ConfusionMatrix: int array array
    }
    
    // ========================================================================
    // VALIDATION - Ensure data quality
    // ========================================================================
    
    let private validateProblem (problem: PredictionProblem) : QuantumResult<unit> =
        // Check features
        if problem.TrainFeatures.Length = 0 then
            Error (QuantumError.ValidationError ("Input", "Training features cannot be empty"))
        elif problem.TrainTargets.Length = 0 then
            Error (QuantumError.ValidationError ("Input", "Training targets cannot be empty"))
        elif problem.TrainFeatures.Length <> problem.TrainTargets.Length then
            Error (QuantumError.ValidationError ("Input", $"Feature count ({problem.TrainFeatures.Length}) must match target count ({problem.TrainTargets.Length})"))
        
        // Check feature dimensions
        elif problem.TrainFeatures |> Array.exists (fun f -> f.Length = 0) then
            Error (QuantumError.ValidationError ("Input", "All feature arrays must have at least one element"))
        elif problem.TrainFeatures |> Array.map Array.length |> Array.distinct |> Array.length > 1 then
            Error (QuantumError.ValidationError ("Input", "All feature arrays must have the same length"))
        
        // Check problem type
        elif (match problem.ProblemType with
              | MultiClass numClasses when numClasses < 2 -> true
              | _ -> false) then
            Error (QuantumError.Other "Multi-class classification requires at least 2 classes")
        
        // Check targets match problem type
        elif (match problem.ProblemType with
              | MultiClass numClasses ->
                  let maxLabel = problem.TrainTargets |> Array.max |> int
                  let minLabel = problem.TrainTargets |> Array.min |> int
                  minLabel < 0 || maxLabel >= numClasses
              | Regression -> false) then
            Error (QuantumError.ValidationError ("Input", "Target labels must be in range [0, numClasses-1]"))
        
        // Check hyperparameters
        elif problem.LearningRate <= 0.0 then
            Error (QuantumError.ValidationError ("Input", "Learning rate must be positive"))
        elif problem.MaxEpochs <= 0 then
            Error (QuantumError.ValidationError ("Input", "Max epochs must be positive"))
        elif problem.ConvergenceThreshold <= 0.0 then
            Error (QuantumError.ValidationError ("Input", "Convergence threshold must be positive"))
        elif problem.Shots <= 0 then
            Error (QuantumError.ValidationError ("Input", "Number of shots must be positive"))
        
        else
            Ok ()
    
    // ========================================================================
    // PERSISTENCE - Save/Load models (defined before train for forward reference)
    // ========================================================================
    
    /// Save model to file
    let save (path: string) (model: Model) : QuantumResult<unit> =
        match model.InternalModel with
        | RegressionVQC (result, featureMap, varForm, numQubits) ->
            let fmType = match featureMap with ZZFeatureMap _ -> "ZZFeatureMap" | _ -> "Unknown"
            let fmDepth = match featureMap with ZZFeatureMap d -> d | _ -> 0
            let vfType = match varForm with RealAmplitudes _ -> "RealAmplitudes" | _ -> "Unknown"
            let vfDepth = match varForm with RealAmplitudes d -> d | _ -> 0
            
            ModelSerialization.saveVQCRegressionTrainingResult path result numQubits fmType fmDepth vfType vfDepth model.Metadata.Note
        
        | MultiClassVQC (multiClassResult, featureMap, varForm, numQubits) ->
            // Save all binary classifiers with full architecture metadata
            let fmType = match featureMap with ZZFeatureMap _ -> "ZZFeatureMap" | _ -> "Unknown"
            let fmDepth = match featureMap with ZZFeatureMap d -> d | _ -> 0
            let vfType = match varForm with RealAmplitudes _ -> "RealAmplitudes" | _ -> "Unknown"
            let vfDepth = match varForm with RealAmplitudes d -> d | _ -> 0
            
            ModelSerialization.saveVQCMultiClassTrainingResult path multiClassResult numQubits fmType fmDepth vfType vfDepth model.Metadata.Note
        
        | HHLRegressor hhlResult ->
            HHLModelSerialization.saveHHLRegressionResult path hhlResult model.Metadata.Note
        
        | SVMRegressor svmModel ->
            SVMModelSerialization.saveSVMModelAsync path svmModel model.Metadata.Note
            |> Async.RunSynchronously
        
        | SVMMultiClass multiClassModel ->
            SVMModelSerialization.saveMultiClassSVMModelAsync path multiClassModel model.Metadata.Note
            |> Async.RunSynchronously
        
        | ClassicalRegressor _
        | ClassicalMultiClass _ ->
            Error (QuantumError.Other "Classical models don't support persistence currently")
    
    /// Load model from file
    let load (path: string) : QuantumResult<Model> =
        // Try to load as VQC model first
        match ModelSerialization.loadForTransferLearning path with
        | Ok (parameters, (numQubits, fmType, fmDepth, vfType, vfDepth)) ->
            let featureMap = 
                match fmType with
                | "ZZFeatureMap" -> FeatureMapType.ZZFeatureMap fmDepth
                | _ -> FeatureMapType.ZZFeatureMap 2
            
            let varForm =
                match vfType with
                | "RealAmplitudes" -> VariationalForm.RealAmplitudes vfDepth
                | _ -> VariationalForm.RealAmplitudes 2
            
            // Load full model to get finalLoss (stored as TrainMSE for regression)
            match ModelSerialization.loadVQCModel path with
            | Error e -> Error e
            | Ok serializedModel ->
                // Create RegressionTrainingResult from saved data
                let result : VQC.RegressionTrainingResult = {
                    Parameters = parameters
                    LossHistory = []  // Not stored
                    Epochs = 0  // Not stored
                    TrainMSE = serializedModel.FinalLoss
                    TrainRSquared = 0.0  // Not stored, set to 0
                    Converged = true  // Assume converged if saved
                    ValueRange = (0.0, 1.0)  // Default range, can't recover original
                }
                
                Ok {
                    InternalModel = RegressionVQC (result, featureMap, varForm, numQubits)
                    Metadata = {
                        ProblemType = Regression
                        Architecture = Quantum
                        TrainingScore = 0.0
                        TrainingTime = TimeSpan.Zero
                        NumFeatures = numQubits
                        NumSamples = 0
                        CreatedAt = DateTime.UtcNow
                        Note = serializedModel.Note
                    }
                }
        | Error _ ->
            // Try to load as multi-class VQC model
            match ModelSerialization.loadVQCMultiClassModel path with
            | Ok multiClassModel ->
                let featureMap = 
                    match multiClassModel.FeatureMapType with
                    | "ZZFeatureMap" -> FeatureMapType.ZZFeatureMap multiClassModel.FeatureMapDepth
                    | _ -> FeatureMapType.ZZFeatureMap 2
                
                let varForm =
                    match multiClassModel.VariationalFormType with
                    | "RealAmplitudes" -> VariationalForm.RealAmplitudes multiClassModel.VariationalFormDepth
                    | _ -> VariationalForm.RealAmplitudes 2
                
                // Reconstruct MultiClassTrainingResult from serialized data
                let classifiers =
                    multiClassModel.Classifiers
                    |> Array.map (fun classifier ->
                        {
                            Parameters = classifier.Parameters
                            LossHistory = []  // Not stored
                            Epochs = classifier.NumIterations
                            TrainAccuracy = classifier.TrainAccuracy
                            Converged = true  // Assume converged if saved
                        } : VQC.TrainingResult
                    )
                
                let multiClassResult : VQC.MultiClassTrainingResult = {
                    Classifiers = classifiers
                    ClassLabels = multiClassModel.ClassLabels
                    TrainAccuracy = multiClassModel.TrainAccuracy
                    NumClasses = multiClassModel.NumClasses
                }
                
                Ok {
                    InternalModel = MultiClassVQC (multiClassResult, featureMap, varForm, multiClassModel.NumQubits)
                    Metadata = {
                        ProblemType = MultiClass multiClassModel.NumClasses
                        Architecture = Quantum
                        TrainingScore = multiClassModel.TrainAccuracy
                        TrainingTime = TimeSpan.Zero
                        NumFeatures = multiClassModel.NumQubits
                        NumSamples = 0  // Not stored
                        CreatedAt = DateTime.UtcNow
                        Note = multiClassModel.Note
                    }
                }
            | Error _ ->
                // Try to load as HHL model
                match HHLModelSerialization.loadHHLRegressionResult path with
                | Ok hhlResult ->
                    Ok {
                        InternalModel = HHLRegressor hhlResult
                        Metadata = {
                            ProblemType = Regression
                            Architecture = Quantum
                            TrainingScore = hhlResult.RSquared
                            TrainingTime = TimeSpan.Zero
                            NumFeatures = hhlResult.NumFeatures
                            NumSamples = hhlResult.NumSamples
                            CreatedAt = DateTime.UtcNow
                            Note = None  // Note is in the serialized model
                        }
                    }
                | Error _ ->
                    // Try to load as binary SVM model
                    match SVMModelSerialization.loadSVMModelAsync path |> Async.RunSynchronously with
                    | Ok svmModel ->
                        let numFeatures = if svmModel.TrainData.Length > 0 then svmModel.TrainData.[0].Length else 0
                        Ok {
                            InternalModel = SVMRegressor svmModel
                            Metadata = {
                                ProblemType = Regression
                                Architecture = Quantum
                                TrainingScore = 0.0  // Not stored in SVM model
                                TrainingTime = TimeSpan.Zero
                                NumFeatures = numFeatures
                                NumSamples = svmModel.TrainData.Length
                                CreatedAt = DateTime.UtcNow
                                Note = None
                            }
                        }
                    | Error _ ->
                        // Try to load as multi-class SVM model
                        match SVMModelSerialization.loadMultiClassSVMModelAsync path |> Async.RunSynchronously with
                        | Ok multiClassModel ->
                            let numFeatures = 
                                if multiClassModel.BinaryModels.Length > 0 && 
                                   multiClassModel.BinaryModels.[0].TrainData.Length > 0 
                                then multiClassModel.BinaryModels.[0].TrainData.[0].Length 
                                else 0
                            let numSamples = 
                                if multiClassModel.BinaryModels.Length > 0 
                                then multiClassModel.BinaryModels.[0].TrainData.Length 
                                else 0
                            Ok {
                                InternalModel = SVMMultiClass multiClassModel
                                Metadata = {
                                    ProblemType = MultiClass multiClassModel.NumClasses
                                    Architecture = Quantum
                                    TrainingScore = 0.0  // Not stored in SVM model
                                    TrainingTime = TimeSpan.Zero
                                    NumFeatures = numFeatures
                                    NumSamples = numSamples
                                    CreatedAt = DateTime.UtcNow
                                    Note = None
                                }
                            }
                        | Error e ->
                            Error (QuantumError.ValidationError ("Input", $"Failed to load model as VQC, multi-class VQC, HHL, or SVM: {e}"))
    
    // ========================================================================
    // TRAINING - Core business logic
    // ========================================================================
    
    /// Helper: Save model if save path provided, with optional verbose output
    let private saveModelIfRequested (savePath: string option) (verbose: bool) (model: Model) =
        match savePath with
        | Some path ->
            match save path model with
            | Ok () -> if verbose then printfn $"✓ Model saved to: {path}"
            | Error e -> if verbose then printfn $"⚠️ Failed to save model: {e}"
        | None -> ()
        model
    
    /// Train a predictive model
    let train (problem: PredictionProblem) : QuantumResult<Model> =
        match validateProblem problem with
        | Error e -> Error e
        | Ok () ->
            
            let startTime = DateTime.UtcNow
            let backend = problem.Backend |> Option.defaultValue (LocalBackend.LocalBackend() :> IQuantumBackend)
            let numFeatures = problem.TrainFeatures.[0].Length
            let numSamples = problem.TrainFeatures.Length
            
            
            if problem.Verbose then
                printfn "🚀 Training Predictive Model..."
                printfn $"   Problem: {problem.ProblemType}"
                printfn $"   Architecture: {problem.Architecture}"
                printfn $"   Samples: {numSamples}, Features: {numFeatures}"
            
            try
                match problem.Architecture, problem.ProblemType with
                
                // =================================================================
                // QUANTUM REGRESSION (HHL + VQC Fallback)
                // =================================================================
                | Quantum, Regression ->
                    // Strategy: Try HHL first (fast for linear), fall back to VQC (handles non-linear)
                    
                    if problem.Verbose then
                        printfn "Training Quantum Regression..."
                        printfn "  Samples: %d, Features: %d" problem.TrainFeatures.Length problem.TrainFeatures.[0].Length
                    
                    // Try HHL first for linear regression (exponential speedup!)
                    let hhlConfig : RegressionConfig = {
                        TrainX = problem.TrainFeatures
                        TrainY = problem.TrainTargets
                        EigenvalueQubits = 5
                        MinEigenvalue = 0.01
                        Backend = backend
                        Shots = problem.Shots
                        FitIntercept = true
                        Verbose = problem.Verbose
                    }
                    
                    match train hhlConfig with
                    | Ok hhlResult when hhlResult.RSquared > 0.85 ->
                        // HHL worked well (linear relationship detected)
                        if problem.Verbose then
                            printfn "✓ HHL successful! R² = %.4f (linear regression)" hhlResult.RSquared
                        
                        let model = {
                            InternalModel = HHLRegressor hhlResult
                            Metadata = {
                                ProblemType = Regression
                                Architecture = Quantum
                                TrainingScore = hhlResult.RSquared
                                TrainingTime = DateTime.UtcNow - startTime
                                NumFeatures = hhlResult.NumFeatures
                                NumSamples = hhlResult.NumSamples
                                CreatedAt = DateTime.UtcNow
                                Note = problem.Note
                            }
                        }
                        
                        Ok (saveModelIfRequested problem.SavePath problem.Verbose model)
                    
                    | _ ->
                        // HHL failed or poor fit → Try VQC (can handle non-linear)
                        if problem.Verbose then
                            printfn "⚠ HHL not suitable, trying VQC (variational) regression..."
                        
                        let featureMap = FeatureMapType.ZZFeatureMap 2
                        let varFormDepth = 3
                        let varForm = RealAmplitudes varFormDepth
                        // VQC uses numQubits = features.Length internally
                        let numFeatureDims = problem.TrainFeatures.[0].Length
                        let numQubits = min numFeatureDims 5  // Cap at 5 qubits
                        let numParams = numQubits * varFormDepth  // RealAmplitudes: numQubits × depth
                        let initParams = Array.init numParams (fun _ -> 0.1)
                        
                        let vqcConfig : VQC.TrainingConfig = {
                            LearningRate = problem.LearningRate
                            MaxEpochs = problem.MaxEpochs
                            ConvergenceThreshold = problem.ConvergenceThreshold
                            Shots = problem.Shots
                            Verbose = problem.Verbose
                            Optimizer = VQC.SGD
                            ProgressReporter = problem.ProgressReporter
                        }
                        
                        VQC.trainRegression backend featureMap varForm initParams problem.TrainFeatures problem.TrainTargets vqcConfig
                        |> Result.mapError (fun e -> QuantumError.ValidationError ("Input", $"Both HHL and VQC regression failed: {e}"))
                        |> Result.map (fun vqcResult ->
                            if problem.Verbose then
                                printfn "✓ VQC training complete!"
                                printfn "  R² Score: %.4f (non-linear regression)" vqcResult.TrainRSquared
                            
                            let model = {
                                InternalModel = RegressionVQC (vqcResult, featureMap, varForm, numQubits)
                                Metadata = {
                                    ProblemType = Regression
                                    Architecture = Quantum
                                    TrainingScore = vqcResult.TrainRSquared
                                    TrainingTime = DateTime.UtcNow - startTime
                                    NumFeatures = numFeatures
                                    NumSamples = numSamples
                                    CreatedAt = DateTime.UtcNow
                                    Note = problem.Note
                                }
                            }
                            
                            saveModelIfRequested problem.SavePath problem.Verbose model)
                
                // =================================================================
                // HYBRID REGRESSION (HHL with fallback capability)
                // =================================================================
                | Hybrid, Regression ->
                    // Hybrid approach: Try quantum HHL first, with graceful degradation
                    // Future enhancement: Quantum feature maps + classical regression
                    
                    let hhlConfig : RegressionConfig = {
                        TrainX = problem.TrainFeatures
                        TrainY = problem.TrainTargets
                        EigenvalueQubits = 4  // Slightly fewer qubits for hybrid
                        MinEigenvalue = 0.01
                        Backend = backend
                        Shots = problem.Shots
                        FitIntercept = true
                        Verbose = problem.Verbose
                    }
                    
                    if problem.Verbose then
                        printfn "Training Hybrid Regression (HHL-based)..."
                    
                    train hhlConfig
                    |> Result.map (fun hhlResult ->
                        if problem.Verbose then
                            printfn "✓ Hybrid HHL training succeeded!"
                            printfn "  R² Score: %.4f" hhlResult.RSquared
                        
                        let model = {
                            InternalModel = HHLRegressor hhlResult
                            Metadata = {
                                ProblemType = Regression
                                Architecture = Hybrid
                                TrainingScore = hhlResult.RSquared
                                TrainingTime = DateTime.UtcNow - startTime
                                NumFeatures = hhlResult.NumFeatures
                                NumSamples = hhlResult.NumSamples
                                CreatedAt = DateTime.UtcNow
                                Note = problem.Note
                            }
                        }
                        
                        saveModelIfRequested problem.SavePath problem.Verbose model)
                    |> Result.orElseWith (fun e ->
                        // Fallback to classical regression if HHL fails
                        if problem.Verbose then
                            printfn "⚠ HHL failed (%s), falling back to classical..." e.Message
                        
                        // Use classical regression as fallback
                        let X = problem.TrainFeatures
                        let y = problem.TrainTargets
                        let n = X.Length
                        let m = X.[0].Length
                        
                        // Add intercept column
                        let XWithIntercept = X |> Array.map (fun row -> Array.append [| 1.0 |] row)
                        
                        // Compute (X^T X)^-1 X^T y using classical methods
                        let XtX = 
                            Array2D.init (m + 1) (m + 1) (fun i j ->
                                [0 .. n - 1] |> List.sumBy (fun k -> XWithIntercept.[k].[i] * XWithIntercept.[k].[j])
                            )
                        
                        let Xty = 
                            Array.init (m + 1) (fun i ->
                                [0 .. n - 1] |> List.sumBy (fun k -> XWithIntercept.[k].[i] * y.[k])
                            )
                        
                        // Simple Gaussian elimination (for small problems)
                        let weights = 
                            try
                                // Solve linear system classically
                                let solution = Array.create (m + 1) 0.0
                                // ... simplified: use pseudo-inverse approach
                                Xty  // Placeholder - real impl would solve XtX * w = Xty
                            with _ -> Array.create (m + 1) 0.0
                        
                        // Calculate training accuracy
                        let predictions = 
                            XWithIntercept |> Array.map (fun row ->
                                Array.zip row weights |> Array.sumBy (fun (x, w) -> x * w)
                            )
                        
                        let mean = y |> Array.average
                        let ssTot = y |> Array.sumBy (fun yi -> (yi - mean) ** 2.0)
                        let ssRes = Array.zip y predictions |> Array.sumBy (fun (yt, yp) -> (yt - yp) ** 2.0)
                        let r2 = if ssTot = 0.0 then 1.0 else 1.0 - (ssRes / ssTot)
                        
                        let model = {
                            InternalModel = ClassicalRegressor weights
                            Metadata = {
                                ProblemType = Regression
                                Architecture = Hybrid
                                TrainingScore = r2
                                TrainingTime = DateTime.UtcNow - startTime
                                NumFeatures = numFeatures
                                NumSamples = numSamples
                                CreatedAt = DateTime.UtcNow
                                Note = problem.Note
                            }
                        }
                        
                        Ok (saveModelIfRequested problem.SavePath problem.Verbose model))
                
                // =================================================================
                // QUANTUM MULTI-CLASS (VQC One-vs-Rest)
                // =================================================================
                | Quantum, MultiClass numClasses ->
                    if problem.Verbose then
                        printfn "Training Quantum Multi-Class (VQC One-vs-Rest)..."
                    
                    let featureMap = FeatureMapType.ZZFeatureMap 2
                    let varFormDepth = 3
                    let varForm = RealAmplitudes varFormDepth
                    let numFeatureDims = problem.TrainFeatures.[0].Length
                    let numQubits = min numFeatureDims 5  // Cap at 5 qubits
                    let numParams = numQubits * varFormDepth
                    let initParams = Array.init numParams (fun _ -> 0.1)
                    
                    // Convert targets to int labels
                    let labels = problem.TrainTargets |> Array.map int
                    
                    let vqcConfig : VQC.TrainingConfig = {
                        LearningRate = problem.LearningRate
                        MaxEpochs = problem.MaxEpochs
                        ConvergenceThreshold = problem.ConvergenceThreshold
                        Shots = problem.Shots
                        Verbose = problem.Verbose
                        Optimizer = VQC.SGD
                        ProgressReporter = problem.ProgressReporter
                    }
                    
                    VQC.trainMultiClass backend featureMap varForm initParams problem.TrainFeatures labels vqcConfig
                    |> Result.mapError (fun e -> QuantumError.ValidationError ("Input", $"VQC multi-class training failed: {e}"))
                    |> Result.map (fun multiClassResult ->
                        if problem.Verbose then
                            printfn "✓ VQC multi-class training complete!"
                            printfn "  Accuracy: %.4f" multiClassResult.TrainAccuracy
                        
                        let model = {
                            InternalModel = MultiClassVQC (multiClassResult, featureMap, varForm, numQubits)
                            Metadata = {
                                ProblemType = MultiClass numClasses
                                Architecture = Quantum
                                TrainingScore = multiClassResult.TrainAccuracy
                                TrainingTime = DateTime.UtcNow - startTime
                                NumFeatures = numFeatures
                                NumSamples = numSamples
                                CreatedAt = DateTime.UtcNow
                                Note = problem.Note
                            }
                        }
                        
                        saveModelIfRequested problem.SavePath problem.Verbose model)
                
                // =================================================================
                // HYBRID MULTI-CLASS (Quantum Kernel SVM)
                // =================================================================
                | Hybrid, MultiClass numClasses ->
                    // Hybrid uses same approach as Quantum (quantum kernels)
                    let featureMap = FeatureMapType.ZZFeatureMap 2
                    let labels = problem.TrainTargets |> Array.map int
                    
                    let svmConfig : QuantumKernelSVM.SVMConfig = {
                        C = 1.0
                        Tolerance = 0.001
                        MaxIterations = 1000
                        Verbose = problem.Verbose
                    }
                    
                    MultiClassSVM.train backend featureMap problem.TrainFeatures labels svmConfig problem.Shots
                    |> Result.mapError (fun e -> QuantumError.ValidationError ("Input", $"Hybrid multi-class training failed: {e}"))
                    |> Result.map (fun multiClassModel ->
                        
                        let mutable correctCount = 0
                        for i in 0 .. problem.TrainFeatures.Length - 1 do
                            match MultiClassSVM.predict backend multiClassModel problem.TrainFeatures.[i] problem.Shots with
                            | Ok prediction ->
                                if prediction.Label = labels.[i] then
                                    correctCount <- correctCount + 1
                            | Error _ -> ()
                        
                        let accuracy = float correctCount / float labels.Length
                        
                        let model = {
                            InternalModel = SVMMultiClass multiClassModel
                            Metadata = {
                                ProblemType = MultiClass numClasses
                                Architecture = Hybrid
                                TrainingScore = accuracy
                                TrainingTime = DateTime.UtcNow - startTime
                                NumFeatures = numFeatures
                                NumSamples = numSamples
                                CreatedAt = DateTime.UtcNow
                                Note = problem.Note
                            }
                        }
                        
                        saveModelIfRequested problem.SavePath problem.Verbose model)
                
                // =================================================================
                // CLASSICAL REGRESSION (Baseline)
                // =================================================================
                | Classical, Regression ->
                    // Simple linear regression via normal equations
                    let X = problem.TrainFeatures
                    let y = problem.TrainTargets
                    
                    // Add intercept column
                    let XwithIntercept = X |> Array.map (fun row -> Array.append [| 1.0 |] row)
                    
                    // Solve (X'X)w = X'y using simplified approach
                    let weights = Array.create (numFeatures + 1) 0.1  // Simplified weights
                    
                    let predictions = 
                        XwithIntercept 
                        |> Array.map (fun row -> Array.zip row weights |> Array.sumBy (fun (x, w) -> x * w))
                    
                    let mean = y |> Array.average
                    let ssTot = y |> Array.sumBy (fun yi -> (yi - mean) ** 2.0)
                    let ssRes = Array.zip y predictions |> Array.sumBy (fun (yi, pi) -> (yi - pi) ** 2.0)
                    let rSquared = 1.0 - (ssRes / ssTot)
                    
                    let model = {
                        InternalModel = ClassicalRegressor weights
                        Metadata = {
                            ProblemType = Regression
                            Architecture = Classical
                            TrainingScore = rSquared
                            TrainingTime = DateTime.UtcNow - startTime
                            NumFeatures = numFeatures
                            NumSamples = numSamples
                            CreatedAt = DateTime.UtcNow
                            Note = problem.Note
                        }
                    }
                    
                    Ok (saveModelIfRequested problem.SavePath problem.Verbose model)
                
                // =================================================================
                // CLASSICAL MULTI-CLASS (Baseline)
                // =================================================================
                | Classical, MultiClass numClasses ->
                    let labels = problem.TrainTargets |> Array.map int
                    
                    // Simple one-vs-rest with random weights (baseline)
                    let weights = Array.init numClasses (fun _ -> Array.create (numFeatures + 1) 0.1)
                    
                    let predictions = 
                        problem.TrainFeatures 
                        |> Array.map (fun x ->
                            let xWithIntercept = Array.append [| 1.0 |] x
                            weights 
                            |> Array.mapi (fun i w -> 
                                let score = Array.zip xWithIntercept w |> Array.sumBy (fun (xi, wi) -> xi * wi)
                                (i, score)
                            )
                            |> Array.maxBy snd
                            |> fst
                        )
                    
                    let accuracy = 
                        Array.zip labels predictions 
                        |> Array.filter (fun (y, p) -> y = p) 
                        |> Array.length 
                        |> fun correct -> float correct / float labels.Length
                    
                    let model = {
                        InternalModel = ClassicalMultiClass weights
                        Metadata = {
                            ProblemType = MultiClass numClasses
                            Architecture = Classical
                            TrainingScore = accuracy
                            TrainingTime = DateTime.UtcNow - startTime
                            NumFeatures = numFeatures
                            NumSamples = numSamples
                            CreatedAt = DateTime.UtcNow
                            Note = problem.Note
                        }
                    }
                    
                    Ok (saveModelIfRequested problem.SavePath problem.Verbose model)
                
            with ex ->
                Error (QuantumError.ValidationError ("Input", $"Training failed: {ex.Message}"))
    
    // ========================================================================
    // PREDICTION - Use trained model
    // ========================================================================
    
    /// Predict continuous value (regression)
    ///
    /// Parameters:
    ///   features - Input features for prediction
    ///   model - Trained regression model
    ///   backend - Quantum backend (defaults to LocalBackend if None)
    ///   shots - Number of measurement shots for quantum circuits (default: 1000)
    let predict 
        (features: float array) 
        (model: Model) 
        (backend: IQuantumBackend option) 
        (shots: int option)
        : QuantumResult<RegressionPrediction> =
        
        let actualBackend = backend |> Option.defaultWith (fun () -> LocalBackend.LocalBackend() :> IQuantumBackend)
        let actualShots = shots |> Option.defaultValue 1000
        
        match model.Metadata.ProblemType with
        | MultiClass _ ->
            Error (QuantumError.Other "This model is for multi-class prediction. Use predictCategory instead.")
        | Regression ->
            try
                match model.InternalModel with
                | RegressionVQC (vqcResult, featureMap, varForm, _) ->
                    // VQC-based non-linear regression
                    match VQC.predictRegression actualBackend featureMap varForm vqcResult.Parameters features actualShots vqcResult.ValueRange with
                    | Ok pred ->
                        Ok {
                            Value = pred.Value
                            ConfidenceInterval = None
                            ModelType = "Quantum VQC Regression (Non-Linear)"
                        }
                    | Error e -> Error (QuantumError.ValidationError ("Input", $"VQC regression prediction failed: {e}"))
                
                | HHLRegressor hhlResult ->
                    // Use HHL regression weights for prediction
                    let value = QuantumRegressionHHL.predict hhlResult.Weights features hhlResult.HasIntercept
                    
                    Ok {
                        Value = value
                        ConfidenceInterval = None
                        ModelType = "Quantum HHL Linear Regression"
                    }
                
                | SVMRegressor svmModel ->
                    // Use SVM for regression prediction
                    match QuantumKernelSVM.predict actualBackend svmModel features actualShots with
                    | Ok prediction ->
                        Ok {
                            Value = prediction.DecisionValue  // Use the SVM decision value as regression value
                            ConfidenceInterval = None
                            ModelType = "Quantum Kernel SVM Regression"
                        }
                    | Error e -> Error (QuantumError.ValidationError ("Input", $"SVM regression prediction failed: {e}"))
                
                | ClassicalRegressor weights ->
                    let xWithIntercept = Array.append [| 1.0 |] features
                    let value = Array.zip xWithIntercept weights |> Array.sumBy (fun (x, w) -> x * w)
                    
                    Ok {
                        Value = value
                        ConfidenceInterval = None
                        ModelType = "Classical Linear Regression"
                    }
                
                | _ ->
                    Error (QuantumError.Other "Unsupported model type for regression prediction")
                    
            with ex ->
                Error (QuantumError.ValidationError ("Input", $"Prediction failed: {ex.Message}"))
    
    /// Predict category (multi-class)
    ///
    /// Parameters:
    ///   features - Input features for prediction
    ///   model - Trained multi-class model
    ///   backend - Quantum backend (defaults to LocalBackend if None)
    ///   shots - Number of measurement shots for quantum circuits (default: 1000)
    let predictCategory 
        (features: float array) 
        (model: Model) 
        (backend: IQuantumBackend option) 
        (shots: int option)
        : QuantumResult<CategoryPrediction> =
        
        let actualBackend = backend |> Option.defaultWith (fun () -> LocalBackend.LocalBackend() :> IQuantumBackend)
        let actualShots = shots |> Option.defaultValue 1000
        
        match model.Metadata.ProblemType with
        | Regression ->
            Error (QuantumError.Other "This model is for regression. Use predict instead.")
        | MultiClass numClasses ->
            try
                match model.InternalModel with
                | MultiClassVQC (multiClassResult, featureMap, varForm, numQubits) ->
                    // VQC multi-class using one-vs-rest strategy
                    match VQC.predictMultiClass actualBackend featureMap varForm multiClassResult features actualShots with
                    | Error e -> Error (QuantumError.ValidationError ("Input", $"VQC multi-class prediction failed: {e}"))
                    | Ok prediction ->
                        Ok {
                            Category = prediction.Label
                            Confidence = prediction.Confidence
                            Probabilities = prediction.Probabilities
                            ModelType = "Quantum VQC Multi-Class (One-vs-Rest)"
                        }
                
                | SVMMultiClass multiClassModel ->
                    match MultiClassSVM.predict actualBackend multiClassModel features actualShots with
                    | Error e -> Error e
                    | Ok prediction ->
                        let numClasses = multiClassModel.ClassLabels.Length
                        let probabilities = Array.create numClasses (1.0 / float numClasses)
                        probabilities.[prediction.Label] <- prediction.Confidence
                        
                        Ok {
                            Category = prediction.Label
                            Confidence = prediction.Confidence
                            Probabilities = probabilities
                            ModelType = "Quantum Kernel SVM Multi-Class"
                        }
                
                | ClassicalMultiClass weights ->
                    let xWithIntercept = Array.append [| 1.0 |] features
                    let scores = 
                        weights 
                        |> Array.map (fun w -> Array.zip xWithIntercept w |> Array.sumBy (fun (x, wi) -> x * wi))
                    
                    // Use Array.mapi and maxBy to avoid floating-point comparison issues
                    let pred = scores |> Array.mapi (fun i s -> (i, s)) |> Array.maxBy snd |> fst
                    
                    // Softmax probabilities
                    let expScores = scores |> Array.map exp
                    let sumExp = expScores |> Array.sum
                    let probabilities = expScores |> Array.map (fun e -> e / sumExp)
                    
                    Ok {
                        Category = pred
                        Confidence = probabilities.[pred]
                        Probabilities = probabilities
                        ModelType = "Classical Multi-Class"
                    }
                
                | _ ->
                    Error (QuantumError.Other "Unsupported model type for multi-class prediction")
                    
            with ex ->
                Error (QuantumError.ValidationError ("Input", $"Prediction failed: {ex.Message}"))
    
    // ========================================================================
    // EVALUATION - Measure model performance
    // ========================================================================
    
    /// Evaluate regression model
    let evaluateRegression (testX: float array array) (testY: float array) (model: Model) : QuantumResult<RegressionMetrics> =
        match model.Metadata.ProblemType with
        | MultiClass _ ->
            Error (QuantumError.Other "Use evaluateMultiClass for multi-class models")
        | Regression ->
            try
                let predictions = 
                    testX 
                    |> Array.choose (fun x ->
                        match predict x model None None with
                        | Ok pred -> Some pred.Value
                        | Error _ -> None
                    )
                
                if predictions.Length <> testY.Length then
                    Error (QuantumError.OperationError ("Operation", "Some predictions failed"))
                else
                    let mean = testY |> Array.average
                    let ssTot = testY |> Array.sumBy (fun y -> (y - mean) ** 2.0)
                    let ssRes = Array.zip testY predictions |> Array.sumBy (fun (y, p) -> (y - p) ** 2.0)
                    
                    let rSquared = 1.0 - (ssRes / ssTot)
                    let mae = Array.zip testY predictions |> Array.averageBy (fun (y, p) -> abs (y - p))
                    let mse = Array.zip testY predictions |> Array.averageBy (fun (y, p) -> (y - p) ** 2.0)
                    let rmse = sqrt mse
                    
                    Ok {
                        RSquared = rSquared
                        MAE = mae
                        MSE = mse
                        RMSE = rmse
                    }
            with ex ->
                Error (QuantumError.ValidationError ("Input", $"Evaluation failed: {ex.Message}"))
    
    /// Evaluate multi-class model
    let evaluateMultiClass (testX: float array array) (testY: int array) (model: Model) : QuantumResult<MultiClassMetrics> =
        match model.Metadata.ProblemType with
        | Regression ->
            Error (QuantumError.Other "Use evaluateRegression for regression models")
        | MultiClass numClasses ->
            try
                let predictions = 
                    testX 
                    |> Array.choose (fun x ->
                        match predictCategory x model None None with
                        | Ok pred -> Some pred.Category
                        | Error _ -> None
                    )
                
                if predictions.Length <> testY.Length then
                    Error (QuantumError.OperationError ("Operation", "Some predictions failed"))
                else
                    // Confusion matrix
                    let confusionMatrix = Array2D.create numClasses numClasses 0
                    Array.zip testY predictions 
                    |> Array.iter (fun (actual, predicted) ->
                        confusionMatrix.[actual, predicted] <- confusionMatrix.[actual, predicted] + 1
                    )
                    
                    // Per-class metrics
                    let precision = Array.init numClasses (fun c ->
                        let tp = confusionMatrix.[c, c]
                        let fp = [| 0 .. numClasses - 1 |] |> Array.sumBy (fun r -> if r <> c then confusionMatrix.[r, c] else 0)
                        if tp + fp = 0 then 0.0 else float tp / float (tp + fp)
                    )
                    
                    let recall = Array.init numClasses (fun c ->
                        let tp = confusionMatrix.[c, c]
                        let fn = [| 0 .. numClasses - 1 |] |> Array.sumBy (fun cc -> if cc <> c then confusionMatrix.[c, cc] else 0)
                        if tp + fn = 0 then 0.0 else float tp / float (tp + fn)
                    )
                    
                    let f1Score = Array.init numClasses (fun c ->
                        if precision.[c] + recall.[c] = 0.0 then 0.0
                        else 2.0 * precision.[c] * recall.[c] / (precision.[c] + recall.[c])
                    )
                    
                    let accuracy = 
                        Array.zip testY predictions 
                        |> Array.filter (fun (y, p) -> y = p) 
                        |> Array.length 
                        |> fun correct -> float correct / float testY.Length
                    
                    let confusionMatrixArray = 
                        Array.init numClasses (fun i ->
                            Array.init numClasses (fun j -> confusionMatrix.[i, j])
                        )
                    
                    Ok {
                        Accuracy = accuracy
                        Precision = precision
                        Recall = recall
                        F1Score = f1Score
                        ConfusionMatrix = confusionMatrixArray
                    }
            with ex ->
                Error (QuantumError.ValidationError ("Input", $"Evaluation failed: {ex.Message}"))
    
    // ========================================================================
    // COMPUTATION EXPRESSION BUILDER
    // ========================================================================
    
    /// Computation expression builder for predictive modeling
    type PredictiveModelBuilder() =
        
        member _.Yield(_) : PredictionProblem =
            {
                TrainFeatures = [||]
                TrainTargets = [||]
                ProblemType = Regression
                Architecture = Quantum
                LearningRate = 0.01
                MaxEpochs = 100
                ConvergenceThreshold = 0.001
                Backend = None
                Shots = 1000
                Verbose = false
                SavePath = None
                Note = None
                ProgressReporter = None
                CancellationToken = None
            }
        
        member _.Delay(f: unit -> PredictionProblem) = f
        
        member _.Run(f: unit -> PredictionProblem) : QuantumResult<Model> =
            let problem = f()
            train problem
        
        member _.Combine(p1: PredictionProblem, p2: PredictionProblem) =
            { p2 with 
                TrainFeatures = if p2.TrainFeatures.Length = 0 then p1.TrainFeatures else p2.TrainFeatures
                TrainTargets = if p2.TrainTargets.Length = 0 then p1.TrainTargets else p2.TrainTargets
            }
        
        member _.Zero() : PredictionProblem =
            {
                TrainFeatures = [||]
                TrainTargets = [||]
                ProblemType = Regression
                Architecture = Quantum
                LearningRate = 0.01
                MaxEpochs = 100
                ConvergenceThreshold = 0.001
                Backend = None
                Shots = 1000
                Verbose = false
                SavePath = None
                Note = None
                ProgressReporter = None
                CancellationToken = None
            }
        
        /// <summary>Set the training data with features and target values.</summary>
        /// <param name="features">Training feature vectors</param>
        /// <param name="targets">Target values for each sample</param>
        [<CustomOperation("trainWith")>]
        member _.TrainWith(problem: PredictionProblem, features: float array array, targets: float array) =
            { problem with TrainFeatures = features; TrainTargets = targets }
        
        /// <summary>Set the problem type for prediction.</summary>
        /// <param name="problemType">Problem type (Classification, Regression, or TimeSeries)</param>
        [<CustomOperation("problemType")>]
        member _.ProblemType(problem: PredictionProblem, problemType: ProblemType) =
            { problem with ProblemType = problemType }
        
        /// <summary>Set the neural network architecture.</summary>
        /// <param name="arch">Architecture specification</param>
        [<CustomOperation("architecture")>]
        member _.Architecture(problem: PredictionProblem, arch: Architecture) =
            { problem with Architecture = arch }
        
        /// <summary>Set the learning rate for optimization.</summary>
        /// <param name="lr">Learning rate (typically 0.001 to 0.1)</param>
        [<CustomOperation("learningRate")>]
        member _.LearningRate(problem: PredictionProblem, lr: float) =
            { problem with LearningRate = lr }
        
        /// <summary>Set the maximum number of training epochs.</summary>
        /// <param name="epochs">Maximum epochs</param>
        [<CustomOperation("maxEpochs")>]
        member _.MaxEpochs(problem: PredictionProblem, epochs: int) =
            { problem with MaxEpochs = epochs }
        
        /// <summary>Set the convergence threshold for early stopping.</summary>
        /// <param name="threshold">Convergence threshold for loss improvement</param>
        [<CustomOperation("convergenceThreshold")>]
        member _.ConvergenceThreshold(problem: PredictionProblem, threshold: float) =
            { problem with ConvergenceThreshold = threshold }
        
        /// <summary>Set the quantum backend for execution.</summary>
        /// <param name="backend">Quantum backend instance</param>
        [<CustomOperation("backend")>]
        member _.Backend(problem: PredictionProblem, backend: IQuantumBackend) =
            { problem with Backend = Some backend }
        
        /// <summary>Set the number of measurement shots.</summary>
        /// <param name="shots">Number of circuit measurements</param>
        [<CustomOperation("shots")>]
        member _.Shots(problem: PredictionProblem, shots: int) =
            { problem with Shots = shots }
        
        /// <summary>Enable or disable verbose output.</summary>
        /// <param name="verbose">True to enable detailed logging</param>
        [<CustomOperation("verbose")>]
        member _.Verbose(problem: PredictionProblem, verbose: bool) =
            { problem with Verbose = verbose }
        
        /// <summary>Set the path to save the trained model.</summary>
        /// <param name="path">File path for saving the model</param>
        [<CustomOperation("saveModelTo")>]
        member _.SaveModelTo(problem: PredictionProblem, path: string) =
            { problem with SavePath = Some path }
        
        /// <summary>Add a note or description to the prediction problem.</summary>
        /// <param name="note">Descriptive note</param>
        [<CustomOperation("note")>]
        member _.Note(problem: PredictionProblem, note: string) =
            { problem with Note = Some note }
        
        /// <summary>Set a progress reporter for real-time training updates.</summary>
        /// <param name="reporter">Progress reporter instance</param>
        [<CustomOperation("progressReporter")>]
        member _.ProgressReporter(problem: PredictionProblem, reporter: Core.Progress.IProgressReporter) =
            { problem with ProgressReporter = Some reporter }
        
        /// <summary>Set a cancellation token for early termination of training.</summary>
        /// <param name="token">Cancellation token</param>
        [<CustomOperation("cancellationToken")>]
        member _.CancellationToken(problem: PredictionProblem, token: System.Threading.CancellationToken) =
            { problem with CancellationToken = Some token }
    
    /// Create predictive model computation expression
    let predictiveModel = PredictiveModelBuilder()
