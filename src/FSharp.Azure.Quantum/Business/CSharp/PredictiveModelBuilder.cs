using System;
using System.Linq;
using FSharp.Azure.Quantum.Core;
using Microsoft.FSharp.Core;
using static FSharp.Azure.Quantum.Core.BackendAbstraction;

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
        private double[][]? _trainFeatures;
        private double[]? _trainTargets;
        private ProblemType _problemType = ProblemType.Regression;
        private ModelArchitecture _architecture = ModelArchitecture.Quantum;
        private double _learningRate = 0.01;
        private int _maxEpochs = 100;
        private double _convergenceThreshold = 0.001;
        private IQuantumBackend? _backend;
        private int _shots = 1000;
        private bool _verbose;
        private string? _savePath;
        private string? _note;

        /// <summary>
        /// Set training features (samples × features matrix).
        /// Each row is one sample, each column is one feature.
        /// </summary>
        /// <param name="features">Training features matrix.</param>
        /// <returns>The current <see cref="PredictiveModelBuilder"/> instance for chaining.</returns>
        public PredictiveModelBuilder WithFeatures(double[][] features)
        {
            ArgumentNullException.ThrowIfNull(features);
            _trainFeatures = features;
            return this;
        }

        /// <summary>
        /// Set training targets.
        /// For regression: continuous values (e.g., revenue, demand).
        /// For multi-class: integer labels 0, 1, 2, ... (e.g., churn timing categories).
        /// </summary>
        /// <param name="targets">Training targets array.</param>
        /// <returns>The current <see cref="PredictiveModelBuilder"/> instance for chaining.</returns>
        public PredictiveModelBuilder WithTargets(double[] targets)
        {
            ArgumentNullException.ThrowIfNull(targets);
            _trainTargets = targets;
            return this;
        }

        /// <summary>
        /// Set training targets from integer array (convenience for multi-class).
        /// </summary>
        /// <param name="targets">Training targets array.</param>
        /// <returns>The current <see cref="PredictiveModelBuilder"/> instance for chaining.</returns>
        public PredictiveModelBuilder WithTargets(int[] targets)
        {
            ArgumentNullException.ThrowIfNull(targets);
            _trainTargets = targets.Select(t => (double)t).ToArray();
            return this;
        }

        /// <summary>
        /// Specify problem type: Regression or MultiClass.
        /// Default: Regression.
        /// </summary>
        /// <param name="problemType">Problem type to use.</param>
        /// <returns>The current <see cref="PredictiveModelBuilder"/> instance for chaining.</returns>
        public PredictiveModelBuilder WithProblemType(ProblemType problemType)
        {
            _problemType = problemType;
            return this;
        }

        /// <summary>
        /// Choose model architecture (Quantum, Hybrid, or Classical).
        /// Default: Quantum.
        /// </summary>
        /// <param name="architecture">Model architecture to use.</param>
        /// <returns>The current <see cref="PredictiveModelBuilder"/> instance for chaining.</returns>
        public PredictiveModelBuilder WithArchitecture(ModelArchitecture architecture)
        {
            _architecture = architecture;
            return this;
        }

        /// <summary>
        /// Set learning rate for training.
        /// Default: 0.01.
        /// </summary>
        /// <param name="learningRate">Learning rate value.</param>
        /// <returns>The current <see cref="PredictiveModelBuilder"/> instance for chaining.</returns>
        public PredictiveModelBuilder WithLearningRate(double learningRate)
        {
            _learningRate = learningRate;
            return this;
        }

        /// <summary>
        /// Set maximum number of training epochs.
        /// Default: 100.
        /// </summary>
        /// <param name="maxEpochs">Maximum number of epochs.</param>
        /// <returns>The current <see cref="PredictiveModelBuilder"/> instance for chaining.</returns>
        public PredictiveModelBuilder WithMaxEpochs(int maxEpochs)
        {
            _maxEpochs = maxEpochs;
            return this;
        }

        /// <summary>
        /// Set convergence threshold for early stopping.
        /// Default: 0.001.
        /// </summary>
        /// <param name="threshold">Convergence threshold value.</param>
        /// <returns>The current <see cref="PredictiveModelBuilder"/> instance for chaining.</returns>
        public PredictiveModelBuilder WithConvergenceThreshold(double threshold)
        {
            _convergenceThreshold = threshold;
            return this;
        }

        /// <summary>
        /// Specify quantum backend to use.
        /// Default: LocalBackend (simulation).
        /// </summary>
        /// <param name="backend">Quantum backend to use.</param>
        /// <returns>The current <see cref="PredictiveModelBuilder"/> instance for chaining.</returns>
        public PredictiveModelBuilder WithBackend(IQuantumBackend backend)
        {
            ArgumentNullException.ThrowIfNull(backend);
            _backend = backend;
            return this;
        }

        /// <summary>
        /// Set number of measurement shots for quantum circuits.
        /// Default: 1000.
        /// </summary>
        /// <param name="shots">Number of measurement shots.</param>
        /// <returns>The current <see cref="PredictiveModelBuilder"/> instance for chaining.</returns>
        public PredictiveModelBuilder WithShots(int shots)
        {
            _shots = shots;
            return this;
        }

        /// <summary>
        /// Enable verbose logging during training.
        /// Default: false.
        /// </summary>
        /// <param name="verbose">Whether to enable verbose logging.</param>
        /// <returns>The current <see cref="PredictiveModelBuilder"/> instance for chaining.</returns>
        public PredictiveModelBuilder WithVerbose(bool verbose = true)
        {
            _verbose = verbose;
            return this;
        }

        /// <summary>
        /// Save trained model to specified path.
        /// </summary>
        /// <param name="path">Path to save the model.</param>
        /// <returns>The current <see cref="PredictiveModelBuilder"/> instance for chaining.</returns>
        public PredictiveModelBuilder SaveModelTo(string path)
        {
            ArgumentNullException.ThrowIfNull(path);
            _savePath = path;
            return this;
        }

        /// <summary>
        /// Add optional note about the model (saved in metadata).
        /// </summary>
        /// <param name="note">Note to save with the model.</param>
        /// <returns>The current <see cref="PredictiveModelBuilder"/> instance for chaining.</returns>
        public PredictiveModelBuilder WithNote(string note)
        {
            ArgumentNullException.ThrowIfNull(note);
            _note = note;
            return this;
        }

        /// <summary>
        /// Build and train the predictive model.
        /// Returns a trained model ready for predictions.
        /// </summary>
        /// <exception cref="InvalidOperationException">Thrown if training fails.</exception>
        /// <returns>A trained <see cref="IPredictiveModel"/> instance.</returns>
        public IPredictiveModel Build()
        {
            // Convert ProblemType to F# type
            var fsharpProblemType = _problemType.IsMultiClass
                ? PredictiveModel.ProblemType.NewMultiClass(_problemType.NumClasses)
                : PredictiveModel.ProblemType.Regression;

            // Build F# problem specification
            var problem = new PredictiveModel.PredictionProblem(
                _trainFeatures,
                _trainTargets,
                fsharpProblemType,
                ConvertArchitecture(_architecture),
                _learningRate,
                _maxEpochs,
                _convergenceThreshold,
                _backend != null ? FSharpOption<IQuantumBackend>.Some(_backend) : FSharpOption<IQuantumBackend>.None,
                _shots,
                _verbose,
                _savePath != null ? FSharpOption<string>.Some(_savePath) : FSharpOption<string>.None,
                _note != null ? FSharpOption<string>.Some(_note) : FSharpOption<string>.None,
                FSharpOption<Core.Progress.IProgressReporter>.None,
                FSharpOption<System.Threading.CancellationToken>.None);

            // Train model
            var result = PredictiveModel.train(problem);

            if (result.IsError)
            {
                throw new InvalidOperationException($"Training failed: {result.ErrorValue.Message}");
            }

            return new PredictiveModelWrapper(result.ResultValue);
        }

        /// <summary>
        /// Load a previously trained model from file.
        /// </summary>
        /// <param name="path">Path to the saved model file.</param>
        /// <returns>A loaded <see cref="IPredictiveModel"/> instance.</returns>
        public static IPredictiveModel LoadFrom(string path)
        {
            ArgumentNullException.ThrowIfNull(path);
            var result = PredictiveModel.load(path);

            if (result.IsError)
            {
                throw new InvalidOperationException($"Failed to load model: {result.ErrorValue.Message}");
            }

            return new PredictiveModelWrapper(result.ResultValue);
        }

        private static PredictiveModel.Architecture ConvertArchitecture(ModelArchitecture arch)
        {
            return arch switch
            {
                ModelArchitecture.Quantum => PredictiveModel.Architecture.Quantum,
                ModelArchitecture.Hybrid => PredictiveModel.Architecture.Hybrid,
                ModelArchitecture.Classical => PredictiveModel.Architecture.Classical,
                _ => PredictiveModel.Architecture.Quantum,
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
        /// <returns></returns>
        public static ProblemType MultiClass(int numClasses) => new ProblemType
        {
            IsMultiClass = true,
            NumClasses = numClasses,
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
        Classical,
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
        /// <param name="testFeatures">Test features matrix.</param>
        /// <param name="testTargets">Test targets array.</param>
        /// <returns>Regression evaluation metrics.</returns>
        RegressionMetrics EvaluateRegression(double[][] testFeatures, double[] testTargets);

        /// <summary>
        /// Evaluate multi-class model on test set.
        /// </summary>
        /// <param name="testFeatures">Test features matrix.</param>
        /// <param name="testTargets">Test targets array.</param>
        /// <returns>Multi-class evaluation metrics.</returns>
        MultiClassMetrics EvaluateMultiClass(double[][] testFeatures, int[] testTargets);

        /// <summary>
        /// Save model to file.
        /// </summary>
        /// <param name="path">Path to save the model.</param>
        void SaveTo(string path);

        /// <summary>
        /// Gets get model metadata.
        /// </summary>
        PredictiveModelMetadata Metadata { get; }
    }

    /// <summary>
    /// Regression prediction result (no F# types exposed).
    /// </summary>
    public class RegressionPrediction
    {
        /// <summary>Gets predicted value.</summary>
        public double Value { get; init; }

        /// <summary>Gets optional confidence interval (lower, upper).</summary>
        public (double Lower, double Upper)? ConfidenceInterval { get; init; }

        /// <summary>Gets model type used for prediction.</summary>
        public required string ModelType { get; init; }
    }

    /// <summary>
    /// Multi-class category prediction result (no F# types exposed).
    /// </summary>
    public class CategoryPrediction
    {
        /// <summary>Gets predicted category index.</summary>
        public int Category { get; init; }

        /// <summary>Gets confidence score [0, 1].</summary>
        public double Confidence { get; init; }

        /// <summary>Gets mapping of category index to probability.</summary>
        public required double[] Probabilities { get; init; }

        /// <summary>Gets model type used for prediction.</summary>
        public required string ModelType { get; init; }
    }

    /// <summary>
    /// Multi-class evaluation metrics (no F# types exposed).
    /// </summary>
    public class MultiClassMetrics
    {
        /// <summary>Gets overall accuracy.</summary>
        public double Accuracy { get; init; }

        /// <summary>Gets precision per class.</summary>
        public required double[] Precision { get; init; }

        /// <summary>Gets recall per class.</summary>
        public required double[] Recall { get; init; }

        /// <summary>Gets F1 score per class.</summary>
        public required double[] F1Score { get; init; }

        /// <summary>Gets confusion matrix.</summary>
        public required int[][] ConfusionMatrix { get; init; }
    }

    /// <summary>
    /// Predictive model metadata (no F# types exposed).
    /// </summary>
    public class PredictiveModelMetadata
    {
        /// <summary>Gets problem type (Regression or MultiClass).</summary>
        public required string ProblemType { get; init; }

        /// <summary>Gets architecture used for training.</summary>
        public ModelArchitecture Architecture { get; init; }

        /// <summary>Gets training score (R-squared for regression, accuracy for multi-class).</summary>
        public double TrainingScore { get; init; }

        /// <summary>Gets training duration.</summary>
        public TimeSpan TrainingTime { get; init; }

        /// <summary>Gets number of input features.</summary>
        public int NumFeatures { get; init; }

        /// <summary>Gets number of samples used for training.</summary>
        public int NumSamples { get; init; }

        /// <summary>Gets timestamp when the model was created.</summary>
        public DateTime CreatedAt { get; init; }

        /// <summary>Gets optional user note stored with the model.</summary>
        public string? Note { get; init; }
    }

    /// <summary>
    /// Regression evaluation metrics (no F# types exposed).
    /// </summary>
    public class RegressionMetrics
    {
        /// <summary>Gets r² score (coefficient of determination). 1.0 = perfect, 0.0 = baseline.</summary>
        public double RSquared { get; init; }

        /// <summary>Gets mean Absolute Error.</summary>
        public double MAE { get; init; }

        /// <summary>Gets mean Squared Error.</summary>
        public double MSE { get; init; }

        /// <summary>Gets root Mean Squared Error.</summary>
        public double RMSE { get; init; }
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
            var result = PredictiveModel.predict(
                features,
                _model,
                FSharpOption<IQuantumBackend>.None,
                FSharpOption<int>.None);

            if (result.IsError)
            {
                throw new InvalidOperationException($"Prediction failed: {result.ErrorValue.Message}");
            }

            var prediction = result.ResultValue;

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
                ModelType = prediction.ModelType,
            };
        }

        public CategoryPrediction PredictCategory(double[] features)
        {
            var result = PredictiveModel.predictCategory(
                features,
                _model,
                FSharpOption<IQuantumBackend>.None,
                FSharpOption<int>.None);

            if (result.IsError)
            {
                throw new InvalidOperationException($"Prediction failed: {result.ErrorValue.Message}");
            }

            var prediction = result.ResultValue;

            return new CategoryPrediction
            {
                Category = prediction.Category,
                Confidence = prediction.Confidence,
                Probabilities = prediction.Probabilities,
                ModelType = prediction.ModelType,
            };
        }

        public RegressionMetrics EvaluateRegression(double[][] testFeatures, double[] testTargets)
        {
            var result = PredictiveModel.evaluateRegression(testFeatures, testTargets, _model);

            if (result.IsError)
            {
                throw new InvalidOperationException($"Evaluation failed: {result.ErrorValue.Message}");
            }

            var metrics = result.ResultValue;

            return new RegressionMetrics
            {
                RSquared = metrics.RSquared,
                MAE = metrics.MAE,
                MSE = metrics.MSE,
                RMSE = metrics.RMSE,
            };
        }

        public MultiClassMetrics EvaluateMultiClass(double[][] testFeatures, int[] testTargets)
        {
            var result = PredictiveModel.evaluateMultiClass(testFeatures, testTargets, _model);

            if (result.IsError)
            {
                throw new InvalidOperationException($"Evaluation failed: {result.ErrorValue.Message}");
            }

            var metrics = result.ResultValue;

            return new MultiClassMetrics
            {
                Accuracy = metrics.Accuracy,
                Precision = metrics.Precision,
                Recall = metrics.Recall,
                F1Score = metrics.F1Score,
                ConfusionMatrix = metrics.ConfusionMatrix,
            };
        }

        public void SaveTo(string path)
        {
            var result = PredictiveModel.save(path, _model);

            if (result.IsError)
            {
                throw new InvalidOperationException($"Save failed: {result.ErrorValue.Message}");
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
                    Note = FSharpOption<string>.get_IsSome(meta.Note) ? meta.Note.Value : null,
                };
            }
        }
    }
}
