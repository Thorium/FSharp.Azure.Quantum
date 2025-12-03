using System;
using System.Linq;
using FSharp.Azure.Quantum.Core.BackendAbstraction;
using Microsoft.FSharp.Core;

namespace FSharp.Azure.Quantum.Business.CSharp
{
    /// <summary>
    /// C# Fluent API for Predictive Modeling
    /// 
    /// Provides enterprise-friendly API for forecasting and prediction without exposing F# types.
    /// Supports both regression (continuous values) and multi-class classification (categories).
    /// 
    /// Use Cases:
    /// - Customer churn prediction (will customer leave? when?)
    /// - Demand forecasting (how many units will sell?)
    /// - Revenue prediction (expected revenue per customer)
    /// - Customer lifetime value (LTV)
    /// - Risk scoring (credit risk, insurance risk)
    /// - Lead scoring (probability of conversion)
    /// 
    /// Example (Regression):
    /// <code>
    /// var model = new PredictiveModelBuilder()
    ///     .WithFeatures(trainX)
    ///     .WithTargets(trainY)
    ///     .WithProblemType(ProblemType.Regression)
    ///     .Build();
    ///     
    /// var prediction = model.Predict(newCustomer);
    /// Console.WriteLine($"Expected revenue: ${prediction.Value}");
    /// </code>
    /// 
    /// Example (Multi-Class):
    /// <code>
    /// var churnModel = new PredictiveModelBuilder()
    ///     .WithFeatures(customerFeatures)
    ///     .WithTargets(churnLabels)  // 0=Stay, 1=Churn30, 2=Churn60, 3=Churn90
    ///     .WithProblemType(ProblemType.MultiClass(4))
    ///     .SaveModelTo("churn_predictor.model")
    ///     .Build();
    ///     
    /// var pred = churnModel.PredictCategory(customer);
    /// if (pred.Category == 1)
    ///     Console.WriteLine("⚠️ Churn risk in 30 days!");
    /// </code>
    /// </summary>
    public class PredictiveModelBuilder
    {
        private double[][] _trainFeatures;
        private double[] _trainTargets;
        private ProblemType _problemType = ProblemType.Regression;
        private ModelArchitecture _architecture = ModelArchitecture.Quantum;
        private double _learningRate = 0.01;
        private int _maxEpochs = 100;
        private double _convergenceThreshold = 0.001;
        private IQuantumBackend _backend = null;
        private int _shots = 1000;
        private bool _verbose = false;
        private string _savePath = null;
        private string _note = null;

        /// <summary>
        /// Set training features (samples × features matrix).
        /// Each row is one sample, each column is one feature.
        /// </summary>
        public PredictiveModelBuilder WithFeatures(double[][] features)
        {
            _trainFeatures = features;
            return this;
        }

        /// <summary>
        /// Set training targets.
        /// For regression: continuous values (e.g., revenue, demand).
        /// For multi-class: integer labels 0, 1, 2, ... (e.g., churn timing categories).
        /// </summary>
        public PredictiveModelBuilder WithTargets(double[] targets)
        {
            _trainTargets = targets;
            return this;
        }

        /// <summary>
        /// Set training targets from integer array (convenience for multi-class).
        /// </summary>
        public PredictiveModelBuilder WithTargets(int[] targets)
        {
            _trainTargets = targets.Select(t => (double)t).ToArray();
            return this;
        }

        /// <summary>
        /// Specify problem type: Regression or MultiClass.
        /// Default: Regression
        /// </summary>
        public PredictiveModelBuilder WithProblemType(ProblemType problemType)
        {
            _problemType = problemType;
            return this;
        }

        /// <summary>
        /// Choose model architecture (Quantum, Hybrid, or Classical).
        /// Default: Quantum
        /// </summary>
        public PredictiveModelBuilder WithArchitecture(ModelArchitecture architecture)
        {
            _architecture = architecture;
            return this;
        }

        /// <summary>
        /// Set learning rate for training.
        /// Default: 0.01
        /// </summary>
        public PredictiveModelBuilder WithLearningRate(double learningRate)
        {
            _learningRate = learningRate;
            return this;
        }

        /// <summary>
        /// Set maximum number of training epochs.
        /// Default: 100
        /// </summary>
        public PredictiveModelBuilder WithMaxEpochs(int maxEpochs)
        {
            _maxEpochs = maxEpochs;
            return this;
        }

        /// <summary>
        /// Set convergence threshold for early stopping.
        /// Default: 0.001
        /// </summary>
        public PredictiveModelBuilder WithConvergenceThreshold(double threshold)
        {
            _convergenceThreshold = threshold;
            return this;
        }

        /// <summary>
        /// Specify quantum backend to use.
        /// Default: LocalBackend (simulation)
        /// </summary>
        public PredictiveModelBuilder WithBackend(IQuantumBackend backend)
        {
            _backend = backend;
            return this;
        }

        /// <summary>
        /// Set number of measurement shots for quantum circuits.
        /// Default: 1000
        /// </summary>
        public PredictiveModelBuilder WithShots(int shots)
        {
            _shots = shots;
            return this;
        }

        /// <summary>
        /// Enable verbose logging during training.
        /// Default: false
        /// </summary>
        public PredictiveModelBuilder WithVerbose(bool verbose = true)
        {
            _verbose = verbose;
            return this;
        }

        /// <summary>
        /// Save trained model to specified path.
        /// </summary>
        public PredictiveModelBuilder SaveModelTo(string path)
        {
            _savePath = path;
            return this;
        }

        /// <summary>
        /// Add optional note about the model (saved in metadata).
        /// </summary>
        public PredictiveModelBuilder WithNote(string note)
        {
            _note = note;
            return this;
        }

        /// <summary>
        /// Build and train the predictive model.
        /// Returns a trained model ready for predictions.
        /// </summary>
        /// <exception cref="InvalidOperationException">Thrown if training fails.</exception>
        public IPredictiveModel Build()
        {
            // Convert ProblemType to F# type
            var fsharpProblemType = _problemType.IsMultiClass
                ? PredictiveModel.ProblemType.NewMultiClass(_problemType.NumClasses)
                : PredictiveModel.ProblemType.Regression;

            // Build F# problem specification
            var problem = new PredictiveModel.PredictionProblem(
                TrainFeatures: _trainFeatures,
                TrainTargets: _trainTargets,
                ProblemType: fsharpProblemType,
                Architecture: ConvertArchitecture(_architecture),
                LearningRate: _learningRate,
                MaxEpochs: _maxEpochs,
                ConvergenceThreshold: _convergenceThreshold,
                Backend: _backend != null ? FSharpOption<IQuantumBackend>.Some(_backend) : FSharpOption<IQuantumBackend>.None,
                Shots: _shots,
                Verbose: _verbose,
                SavePath: _savePath != null ? FSharpOption<string>.Some(_savePath) : FSharpOption<string>.None,
                Note: _note != null ? FSharpOption<string>.Some(_note) : FSharpOption<string>.None
            );

            // Train model
            var result = PredictiveModel.train(problem);

            if (FSharpResult<PredictiveModel.Model, string>.get_IsError(result))
            {
                var error = ((FSharpResult<PredictiveModel.Model, string>.Error)result).ErrorValue;
                throw new InvalidOperationException($"Training failed: {error}");
            }

            var model = ((FSharpResult<PredictiveModel.Model, string>.Ok)result).ResultValue;
            return new PredictiveModelWrapper(model);
        }

        /// <summary>
        /// Load a previously trained model from file.
        /// </summary>
        public static IPredictiveModel LoadFrom(string path)
        {
            var result = PredictiveModel.load(path);

            if (FSharpResult<PredictiveModel.Model, string>.get_IsError(result))
            {
                var error = ((FSharpResult<PredictiveModel.Model, string>.Error)result).ErrorValue;
                throw new InvalidOperationException($"Failed to load model: {error}");
            }

            var model = ((FSharpResult<PredictiveModel.Model, string>.Ok)result).ResultValue;
            return new PredictiveModelWrapper(model);
        }

        private static PredictiveModel.Architecture ConvertArchitecture(ModelArchitecture arch)
        {
            return arch switch
            {
                ModelArchitecture.Quantum => PredictiveModel.Architecture.Quantum,
                ModelArchitecture.Hybrid => PredictiveModel.Architecture.Hybrid,
                ModelArchitecture.Classical => PredictiveModel.Architecture.Classical,
                _ => PredictiveModel.Architecture.Quantum
            };
        }
    }

    /// <summary>
    /// Problem type for predictive modeling.
    /// </summary>
    public class ProblemType
    {
        /// <summary>
        /// Predict continuous values (revenue, demand, LTV, etc.).
        /// </summary>
        public static readonly ProblemType Regression = new ProblemType { IsMultiClass = false };

        /// <summary>
        /// Predict categories (churn timing, risk levels, etc.).
        /// </summary>
        /// <param name="numClasses">Number of categories.</param>
        public static ProblemType MultiClass(int numClasses) => new ProblemType 
        { 
            IsMultiClass = true, 
            NumClasses = numClasses 
        };

        internal bool IsMultiClass { get; init; }
        internal int NumClasses { get; init; }
    }

    /// <summary>
    /// Architecture choice for predictive modeling.
    /// </summary>
    public enum ModelArchitecture
    {
        /// <summary>Pure quantum model using variational quantum circuits.</summary>
        Quantum,
        
        /// <summary>Hybrid quantum-classical using quantum kernel SVM.</summary>
        Hybrid,
        
        /// <summary>Classical baseline for comparison.</summary>
        Classical
    }

    /// <summary>
    /// Interface for trained predictive model.
    /// Provides simple API for predictions without F# types.
    /// </summary>
    public interface IPredictiveModel
    {
        /// <summary>
        /// Predict continuous value (regression only).
        /// </summary>
        /// <param name="features">Feature vector to predict.</param>
        /// <returns>Regression prediction with value and confidence.</returns>
        /// <exception cref="InvalidOperationException">If model is not regression type.</exception>
        RegressionPrediction Predict(double[] features);

        /// <summary>
        /// Predict category (multi-class only).
        /// </summary>
        /// <param name="features">Feature vector to classify.</param>
        /// <returns>Category prediction with probabilities.</returns>
        /// <exception cref="InvalidOperationException">If model is not multi-class type.</exception>
        CategoryPrediction PredictCategory(double[] features);

        /// <summary>
        /// Evaluate regression model on test set.
        /// </summary>
        RegressionMetrics EvaluateRegression(double[][] testFeatures, double[] testTargets);

        /// <summary>
        /// Evaluate multi-class model on test set.
        /// </summary>
        MultiClassMetrics EvaluateMultiClass(double[][] testFeatures, int[] testTargets);

        /// <summary>
        /// Save model to file.
        /// </summary>
        void SaveTo(string path);

        /// <summary>
        /// Get model metadata.
        /// </summary>
        PredictiveModelMetadata Metadata { get; }
    }

    /// <summary>
    /// Regression prediction result (no F# types exposed).
    /// </summary>
    public class RegressionPrediction
    {
        /// <summary>Predicted value (e.g., revenue, demand, LTV).</summary>
        public double Value { get; init; }

        /// <summary>Confidence interval (if available).</summary>
        public (double Lower, double Upper)? ConfidenceInterval { get; init; }

        /// <summary>Model type used for prediction.</summary>
        public string ModelType { get; init; }
    }

    /// <summary>
    /// Multi-class prediction result (no F# types exposed).
    /// </summary>
    public class CategoryPrediction
    {
        /// <summary>Predicted category (class index: 0, 1, 2, ...).</summary>
        public int Category { get; init; }

        /// <summary>Confidence score for predicted category [0, 1].</summary>
        public double Confidence { get; init; }

        /// <summary>Probability distribution over all categories.</summary>
        public double[] Probabilities { get; init; }

        /// <summary>Model type used for prediction.</summary>
        public string ModelType { get; init; }
    }

    /// <summary>
    /// Regression evaluation metrics (no F# types exposed).
    /// </summary>
    public class RegressionMetrics
    {
        /// <summary>R² score (coefficient of determination). 1.0 = perfect, 0.0 = baseline.</summary>
        public double RSquared { get; init; }

        /// <summary>Mean Absolute Error.</summary>
        public double MAE { get; init; }

        /// <summary>Mean Squared Error.</summary>
        public double MSE { get; init; }

        /// <summary>Root Mean Squared Error.</summary>
        public double RMSE { get; init; }
    }

    /// <summary>
    /// Multi-class evaluation metrics (no F# types exposed).
    /// </summary>
    public class MultiClassMetrics
    {
        /// <summary>Overall accuracy.</summary>
        public double Accuracy { get; init; }

        /// <summary>Precision per class.</summary>
        public double[] Precision { get; init; }

        /// <summary>Recall per class.</summary>
        public double[] Recall { get; init; }

        /// <summary>F1 score per class.</summary>
        public double[] F1Score { get; init; }

        /// <summary>Confusion matrix (actual × predicted).</summary>
        public int[][] ConfusionMatrix { get; init; }
    }

    /// <summary>
    /// Model metadata (no F# types exposed).
    /// </summary>
    public class PredictiveModelMetadata
    {
        public string ProblemType { get; init; }
        public ModelArchitecture Architecture { get; init; }
        public double TrainingScore { get; init; }
        public TimeSpan TrainingTime { get; init; }
        public int NumFeatures { get; init; }
        public int NumSamples { get; init; }
        public DateTime CreatedAt { get; init; }
        public string Note { get; init; }
    }

    // ============================================================================
    // INTERNAL WRAPPER - Hides F# types from C# consumers
    // ============================================================================

    internal class PredictiveModelWrapper : IPredictiveModel
    {
        private readonly PredictiveModel.Model _model;

        public PredictiveModelWrapper(PredictiveModel.Model model)
        {
            _model = model;
        }

        public RegressionPrediction Predict(double[] features)
        {
            var result = PredictiveModel.predict(features, _model);

            if (FSharpResult<PredictiveModel.RegressionPrediction, string>.get_IsError(result))
            {
                var error = ((FSharpResult<PredictiveModel.RegressionPrediction, string>.Error)result).ErrorValue;
                throw new InvalidOperationException($"Prediction failed: {error}");
            }

            var prediction = ((FSharpResult<PredictiveModel.RegressionPrediction, string>.Ok)result).ResultValue;

            (double, double)? confidenceInterval = null;
            if (FSharpOption<Tuple<double, double>>.get_IsSome(prediction.ConfidenceInterval))
            {
                var ci = prediction.ConfidenceInterval.Value;
                confidenceInterval = (ci.Item1, ci.Item2);
            }

            return new RegressionPrediction
            {
                Value = prediction.Value,
                ConfidenceInterval = confidenceInterval,
                ModelType = prediction.ModelType
            };
        }

        public CategoryPrediction PredictCategory(double[] features)
        {
            var result = PredictiveModel.predictCategory(features, _model);

            if (FSharpResult<PredictiveModel.CategoryPrediction, string>.get_IsError(result))
            {
                var error = ((FSharpResult<PredictiveModel.CategoryPrediction, string>.Error)result).ErrorValue;
                throw new InvalidOperationException($"Prediction failed: {error}");
            }

            var prediction = ((FSharpResult<PredictiveModel.CategoryPrediction, string>.Ok)result).ResultValue;

            return new CategoryPrediction
            {
                Category = prediction.Category,
                Confidence = prediction.Confidence,
                Probabilities = prediction.Probabilities,
                ModelType = prediction.ModelType
            };
        }

        public RegressionMetrics EvaluateRegression(double[][] testFeatures, double[] testTargets)
        {
            var result = PredictiveModel.evaluateRegression(testFeatures, testTargets, _model);

            if (FSharpResult<PredictiveModel.RegressionMetrics, string>.get_IsError(result))
            {
                var error = ((FSharpResult<PredictiveModel.RegressionMetrics, string>.Error)result).ErrorValue;
                throw new InvalidOperationException($"Evaluation failed: {error}");
            }

            var metrics = ((FSharpResult<PredictiveModel.RegressionMetrics, string>.Ok)result).ResultValue;

            return new RegressionMetrics
            {
                RSquared = metrics.RSquared,
                MAE = metrics.MAE,
                MSE = metrics.MSE,
                RMSE = metrics.RMSE
            };
        }

        public MultiClassMetrics EvaluateMultiClass(double[][] testFeatures, int[] testTargets)
        {
            var result = PredictiveModel.evaluateMultiClass(testFeatures, testTargets, _model);

            if (FSharpResult<PredictiveModel.MultiClassMetrics, string>.get_IsError(result))
            {
                var error = ((FSharpResult<PredictiveModel.MultiClassMetrics, string>.Error)result).ErrorValue;
                throw new InvalidOperationException($"Evaluation failed: {error}");
            }

            var metrics = ((FSharpResult<PredictiveModel.MultiClassMetrics, string>.Ok)result).ResultValue;

            return new MultiClassMetrics
            {
                Accuracy = metrics.Accuracy,
                Precision = metrics.Precision,
                Recall = metrics.Recall,
                F1Score = metrics.F1Score,
                ConfusionMatrix = metrics.ConfusionMatrix
            };
        }

        public void SaveTo(string path)
        {
            var result = PredictiveModel.save(path, _model);

            if (FSharpResult<Unit, string>.get_IsError(result))
            {
                var error = ((FSharpResult<Unit, string>.Error)result).ErrorValue;
                throw new InvalidOperationException($"Save failed: {error}");
            }
        }

        public PredictiveModelMetadata Metadata
        {
            get
            {
                var meta = _model.Metadata;
                
                var problemTypeStr = meta.ProblemType.IsRegression
                    ? "Regression"
                    : $"MultiClass({((PredictiveModel.ProblemType.MultiClass)meta.ProblemType).Item})";

                var arch = meta.Architecture.IsQuantum ? ModelArchitecture.Quantum
                    : meta.Architecture.IsHybrid ? ModelArchitecture.Hybrid
                    : ModelArchitecture.Classical;

                return new PredictiveModelMetadata
                {
                    ProblemType = problemTypeStr,
                    Architecture = arch,
                    TrainingScore = meta.TrainingScore,
                    TrainingTime = meta.TrainingTime,
                    NumFeatures = meta.NumFeatures,
                    NumSamples = meta.NumSamples,
                    CreatedAt = meta.CreatedAt,
                    Note = FSharpOption<string>.get_IsSome(meta.Note) ? meta.Note.Value : null
                };
            }
        }
    }
}
