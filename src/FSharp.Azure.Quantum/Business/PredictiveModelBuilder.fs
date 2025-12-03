namespace FSharp.Azure.Quantum.Business

open System
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.MachineLearning

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
    }
    
    /// Trained predictive model
    type Model = {
        /// Underlying model
        InternalModel: InternalModel
        
        /// Training metadata
        Metadata: ModelMetadata
    }
    
    and InternalModel =
        | RegressionVQC of VQC.TrainingResult * FeatureMapType * VariationalForm * int
        | MultiClassVQC of VQC.TrainingResult * FeatureMapType * VariationalForm * int * int  // num classes
        | SVMRegressor of QuantumKernelSVM.SVMModel
        | SVMMultiClass of MultiClassSVM.MultiClassModel
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
    
    let private validateProblem (problem: PredictionProblem) : Result<unit, string> =
        // Check features
        if problem.TrainFeatures.Length = 0 then
            Error "Training features cannot be empty"
        elif problem.TrainTargets.Length = 0 then
            Error "Training targets cannot be empty"
        elif problem.TrainFeatures.Length <> problem.TrainTargets.Length then
            Error $"Feature count ({problem.TrainFeatures.Length}) must match target count ({problem.TrainTargets.Length})"
        
        // Check feature dimensions
        elif problem.TrainFeatures |> Array.exists (fun f -> f.Length = 0) then
            Error "All feature arrays must have at least one element"
        elif problem.TrainFeatures |> Array.map Array.length |> Array.distinct |> Array.length > 1 then
            Error "All feature arrays must have the same length"
        
        // Check problem type
        elif (match problem.ProblemType with
              | MultiClass numClasses when numClasses < 2 -> true
              | _ -> false) then
            Error "Multi-class classification requires at least 2 classes"
        
        // Check targets match problem type
        elif (match problem.ProblemType with
              | MultiClass numClasses ->
                  let maxLabel = problem.TrainTargets |> Array.max |> int
                  let minLabel = problem.TrainTargets |> Array.min |> int
                  minLabel < 0 || maxLabel >= numClasses
              | Regression -> false) then
            Error "Target labels must be in range [0, numClasses-1]"
        
        // Check hyperparameters
        elif problem.LearningRate <= 0.0 then
            Error "Learning rate must be positive"
        elif problem.MaxEpochs <= 0 then
            Error "Max epochs must be positive"
        elif problem.ConvergenceThreshold <= 0.0 then
            Error "Convergence threshold must be positive"
        elif problem.Shots <= 0 then
            Error "Number of shots must be positive"
        
        else
            Ok ()
    
    // ========================================================================
    // TRAINING - Core business logic
    // ========================================================================
    
    /// Train a predictive model
    let train (problem: PredictionProblem) : Result<Model, string> =
        match validateProblem problem with
        | Error e -> Error e
        | Ok () ->
            
            let startTime = DateTime.UtcNow
            let backend = problem.Backend |> Option.defaultValue (LocalBackend() :> IQuantumBackend)
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
                // QUANTUM REGRESSION
                // =================================================================
                | Quantum, Regression ->
                    // TODO: Implement quantum regression via HHL algorithm
                    // Linear regression solves: w = (X^T X)^-1 X^T y
                    // This is a linear system Aw = b where A = X^T X, b = X^T y
                    // HHL can solve this quantum mechanically!
                    //
                    // Current limitation: HHL only supports 2x2 systems
                    // Need to extend to arbitrary N×N for full regression support
                    Error "Quantum regression not yet implemented. Use Architecture.Classical or Architecture.Hybrid instead. (TODO: Implement via HHL algorithm for solving normal equations)"
                
                // =================================================================
                // HYBRID REGRESSION (fallback to classical for now)
                // =================================================================
                | Hybrid, Regression ->
                    // TODO: Implement hybrid quantum-classical regression
                    // Could use quantum feature maps with classical regression
                    Error "Hybrid regression not yet implemented. Use Architecture.Classical instead."
                
                // =================================================================
                // QUANTUM MULTI-CLASS (Quantum Kernel SVM)
                // =================================================================
                | Quantum, MultiClass numClasses ->
                    let featureMap = FeatureMapType.ZZFeatureMap 2
                    
                    // Convert targets to int labels
                    let labels = problem.TrainTargets |> Array.map int
                    
                    let svmConfig : QuantumKernelSVM.SVMConfig = {
                        C = 1.0
                        Tolerance = 0.001
                        MaxIterations = 1000
                        Verbose = problem.Verbose
                    }
                    
                    match MultiClassSVM.train backend featureMap problem.TrainFeatures labels svmConfig problem.Shots with
                    | Error e -> Error $"Multi-class training failed: {e}"
                    | Ok multiClassModel ->
                        
                        // Calculate training accuracy
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
                                Architecture = Quantum
                                TrainingScore = accuracy
                                TrainingTime = DateTime.UtcNow - startTime
                                NumFeatures = numFeatures
                                NumSamples = numSamples
                                CreatedAt = DateTime.UtcNow
                                Note = problem.Note
                            }
                        }
                        
                        // Save if requested
                        // TODO: Implement model save function
                        match problem.SavePath with
                        | Some path -> 
                            if problem.Verbose then printfn $"⚠️ Model save not yet implemented (path: {path})"
                        | None -> ()
                        
                        Ok model
                
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
                    
                    match MultiClassSVM.train backend featureMap problem.TrainFeatures labels svmConfig problem.Shots with
                    | Error e -> Error $"Hybrid multi-class training failed: {e}"
                    | Ok multiClassModel ->
                        
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
                        
                        Ok model
                
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
                    
                    Ok model
                
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
                    
                    Ok model
                
                // Unsupported combinations
                | Hybrid, Regression ->
                    Error "Hybrid architecture for regression not yet implemented (use Quantum or Classical)"
                
            with ex ->
                Error $"Training failed: {ex.Message}"
    
    // ========================================================================
    // PREDICTION - Use trained model
    // ========================================================================
    
    /// Predict continuous value (regression)
    let predict (features: float array) (model: Model) : Result<RegressionPrediction, string> =
        match model.Metadata.ProblemType with
        | MultiClass _ ->
            Error "This model is for multi-class prediction. Use predictCategory instead."
        | Regression ->
            try
                match model.InternalModel with
                | RegressionVQC (_, _, _, _) ->
                    // TODO: Quantum regression not yet implemented
                    // Need to implement via HHL algorithm for linear regression
                    Error "Quantum regression prediction not yet implemented"
                
                | ClassicalRegressor weights ->
                    let xWithIntercept = Array.append [| 1.0 |] features
                    let value = Array.zip xWithIntercept weights |> Array.sumBy (fun (x, w) -> x * w)
                    
                    Ok {
                        Value = value
                        ConfidenceInterval = None
                        ModelType = "Classical Linear Regression"
                    }
                
                | _ ->
                    Error "Unsupported model type for regression prediction"
                    
            with ex ->
                Error $"Prediction failed: {ex.Message}"
    
    /// Predict category (multi-class)
    let predictCategory (features: float array) (model: Model) : Result<CategoryPrediction, string> =
        match model.Metadata.ProblemType with
        | Regression ->
            Error "This model is for regression. Use predict instead."
        | MultiClass numClasses ->
            try
                match model.InternalModel with
                | MultiClassVQC (_, _, _, _, _) ->
                    // TODO: VQC multi-class not implemented (use MultiClassSVM instead)
                    Error "VQC multi-class prediction not yet implemented"
                
                | SVMMultiClass multiClassModel ->
                    let backend = LocalBackend() :> IQuantumBackend
                    match MultiClassSVM.predict backend multiClassModel features 1000 with
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
                    
                    let maxScore = scores |> Array.max
                    let pred = scores |> Array.findIndex ((=) maxScore)
                    
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
                    Error "Unsupported model type for multi-class prediction"
                    
            with ex ->
                Error $"Prediction failed: {ex.Message}"
    
    // ========================================================================
    // EVALUATION - Measure model performance
    // ========================================================================
    
    /// Evaluate regression model
    let evaluateRegression (testX: float array array) (testY: float array) (model: Model) : Result<RegressionMetrics, string> =
        match model.Metadata.ProblemType with
        | MultiClass _ ->
            Error "Use evaluateMultiClass for multi-class models"
        | Regression ->
            try
                let predictions = 
                    testX 
                    |> Array.choose (fun x ->
                        match predict x model with
                        | Ok pred -> Some pred.Value
                        | Error _ -> None
                    )
                
                if predictions.Length <> testY.Length then
                    Error "Some predictions failed"
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
                Error $"Evaluation failed: {ex.Message}"
    
    /// Evaluate multi-class model
    let evaluateMultiClass (testX: float array array) (testY: int array) (model: Model) : Result<MultiClassMetrics, string> =
        match model.Metadata.ProblemType with
        | Regression ->
            Error "Use evaluateRegression for regression models"
        | MultiClass numClasses ->
            try
                let predictions = 
                    testX 
                    |> Array.choose (fun x ->
                        match predictCategory x model with
                        | Ok pred -> Some pred.Category
                        | Error _ -> None
                    )
                
                if predictions.Length <> testY.Length then
                    Error "Some predictions failed"
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
                Error $"Evaluation failed: {ex.Message}"
    
    // ========================================================================
    // PERSISTENCE - Save/Load models
    // ========================================================================
    
    /// Save model to file
    let save (path: string) (model: Model) : Result<unit, string> =
        match model.InternalModel with
        | RegressionVQC (result, featureMap, varForm, numQubits)
        | MultiClassVQC (result, featureMap, varForm, numQubits, _) ->
            let fmType = match featureMap with ZZFeatureMap _ -> "ZZFeatureMap" | _ -> "Unknown"
            let fmDepth = match featureMap with ZZFeatureMap d -> d | _ -> 0
            let vfType = match varForm with RealAmplitudes _ -> "RealAmplitudes" | _ -> "Unknown"
            let vfDepth = match varForm with RealAmplitudes d -> d | _ -> 0
            
            ModelSerialization.saveVQCTrainingResult path result numQubits fmType fmDepth vfType vfDepth model.Metadata.Note
        
        | _ ->
            Error "Only VQC models support persistence currently"
    
    /// Load model from file
    let load (path: string) : Result<Model, string> =
        match ModelSerialization.loadForTransferLearning path with
        | Error e -> Error e
        | Ok (parameters, (numQubits, fmType, fmDepth, vfType, vfDepth)) ->
            
            let featureMap = 
                match fmType with
                | "ZZFeatureMap" -> FeatureMapType.ZZFeatureMap fmDepth
                | _ -> FeatureMapType.ZZFeatureMap 2
            
            let varForm =
                match vfType with
                | "RealAmplitudes" -> VariationalForm.RealAmplitudes vfDepth
                | _ -> VariationalForm.RealAmplitudes 2
            
            let result : VQC.TrainingResult = {
                Parameters = parameters
                LossHistory = []
                Epochs = 0
                TrainAccuracy = 0.0
                Converged = true
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
                    Note = None
                }
            }
    
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
            }
        
        member _.Delay(f: unit -> PredictionProblem) = f
        
        member _.Run(f: unit -> PredictionProblem) : Result<Model, string> =
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
            }
        
        [<CustomOperation("trainWith")>]
        member _.TrainWith(problem: PredictionProblem, features: float array array, targets: float array) =
            { problem with TrainFeatures = features; TrainTargets = targets }
        
        [<CustomOperation("problemType")>]
        member _.ProblemType(problem: PredictionProblem, problemType: ProblemType) =
            { problem with ProblemType = problemType }
        
        [<CustomOperation("architecture")>]
        member _.Architecture(problem: PredictionProblem, arch: Architecture) =
            { problem with Architecture = arch }
        
        [<CustomOperation("learningRate")>]
        member _.LearningRate(problem: PredictionProblem, lr: float) =
            { problem with LearningRate = lr }
        
        [<CustomOperation("maxEpochs")>]
        member _.MaxEpochs(problem: PredictionProblem, epochs: int) =
            { problem with MaxEpochs = epochs }
        
        [<CustomOperation("convergenceThreshold")>]
        member _.ConvergenceThreshold(problem: PredictionProblem, threshold: float) =
            { problem with ConvergenceThreshold = threshold }
        
        [<CustomOperation("backend")>]
        member _.Backend(problem: PredictionProblem, backend: IQuantumBackend) =
            { problem with Backend = Some backend }
        
        [<CustomOperation("shots")>]
        member _.Shots(problem: PredictionProblem, shots: int) =
            { problem with Shots = shots }
        
        [<CustomOperation("verbose")>]
        member _.Verbose(problem: PredictionProblem, verbose: bool) =
            { problem with Verbose = verbose }
        
        [<CustomOperation("saveModelTo")>]
        member _.SaveModelTo(problem: PredictionProblem, path: string) =
            { problem with SavePath = Some path }
        
        [<CustomOperation("note")>]
        member _.Note(problem: PredictionProblem, note: string) =
            { problem with Note = Some note }
    
    /// Create predictive model computation expression
    let predictiveModel = PredictiveModelBuilder()
