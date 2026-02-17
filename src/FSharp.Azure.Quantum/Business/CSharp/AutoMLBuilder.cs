using System;
using System.Linq;
using FSharp.Azure.Quantum.Core;
using Microsoft.FSharp.Collections;
using Microsoft.FSharp.Core;
using static FSharp.Azure.Quantum.Core.BackendAbstraction;

namespace FSharp.Azure.Quantum.Business.CSharp
{
    /// <summary>
    /// C# Fluent API for Automated Machine Learning (AutoML)
    ///
    /// The simplest possible ML API - just provide your data and AutoML finds the best model automatically.
    ///
    /// What AutoML Does:
    /// 1. Analyzes your data to understand the problem type
    /// 2. Tries multiple model types (binary/multi-class classification, regression, anomaly detection)
    /// 3. Tests different architectures (Quantum, Hybrid, Classical)
    /// 4. Tunes hyperparameters automatically
    /// 5. Returns the best performing model with a detailed report
    ///
    /// Perfect For:
    /// - Quick prototyping: "Just give me a working model"
    /// - Non-experts: Don't know which algorithm to use
    /// - Baseline comparison: See what's possible
    /// - Model selection: Which approach works best?
    ///
    /// Example:
    /// <code>
    /// // Minimal usage - AutoML figures everything out
    /// var result = new AutoMLBuilder()
    ///     .WithData(features, labels)
    ///     .Build();
    ///
    /// Console.WriteLine($"Best model: {result.BestModelType}");
    /// Console.WriteLine($"Score: {result.Score * 100:F2}%");
    ///
    /// var prediction = result.Predict(newSample);
    /// </code>
    /// </summary>
    public class AutoMLBuilder
    {
        private double[][]? _trainFeatures;
        private double[]? _trainLabels;
        private bool _tryBinaryClassification = true;
        private int? _tryMultiClass;  // Auto-detect
        private bool _tryAnomalyDetection = true;
        private bool _tryRegression = true;
        private bool _trySimilaritySearch;
        private SearchArchitecture[] _tryArchitectures = new[]
        {
            SearchArchitecture.Quantum,
            SearchArchitecture.Hybrid,
            SearchArchitecture.Classical,
        };

        private int _maxTrials = 20;
        private int? _maxTimeMinutes;
        private double _validationSplit = 0.2;
        private IQuantumBackend? _backend;
        private bool _verbose;
        private string? _savePath;
        private int? _randomSeed;

        /// <summary>
        /// Set training data (features and labels).
        /// AutoML will automatically detect the problem type from your labels.
        /// </summary>
        /// <param name="features">Training features matrix.</param>
        /// <param name="labels">Training labels array.</param>
        /// <returns>The builder instance for chaining.</returns>
        public AutoMLBuilder WithData(double[][] features, double[] labels)
        {
            ArgumentNullException.ThrowIfNull(features);
            ArgumentNullException.ThrowIfNull(labels);
            _trainFeatures = features;
            _trainLabels = labels;
            return this;
        }

        /// <summary>
        /// Set training data with integer labels (convenience for classification).
        /// </summary>
        /// <param name="features">Training features matrix.</param>
        /// <param name="labels">Training labels array.</param>
        /// <returns>The builder instance for chaining.</returns>
        public AutoMLBuilder WithData(double[][] features, int[] labels)
        {
            ArgumentNullException.ThrowIfNull(features);
            ArgumentNullException.ThrowIfNull(labels);
            _trainFeatures = features;
            _trainLabels = labels.Select(l => (double)l).ToArray();
            return this;
        }

        /// <summary>
        /// Enable/disable binary classification trials.
        /// Default: true (enabled if data has 2 unique labels).
        /// </summary>
        /// <param name="enable">Whether to enable binary classification trials.</param>
        /// <returns>The builder instance for chaining.</returns>
        public AutoMLBuilder TryBinaryClassification(bool enable = true)
        {
            _tryBinaryClassification = enable;
            return this;
        }

        /// <summary>
        /// Enable multi-class classification trials with specified number of classes.
        /// If not set, AutoML will auto-detect from labels.
        /// </summary>
        /// <param name="numClasses">Number of classes for multi-class classification.</param>
        /// <returns>The builder instance for chaining.</returns>
        public AutoMLBuilder TryMultiClass(int numClasses)
        {
            _tryMultiClass = numClasses;
            return this;
        }

        /// <summary>
        /// Enable/disable anomaly detection trials.
        /// Default: true.
        /// </summary>
        /// <param name="enable">Whether to enable anomaly detection trials.</param>
        /// <returns>The builder instance for chaining.</returns>
        public AutoMLBuilder TryAnomalyDetection(bool enable = true)
        {
            _tryAnomalyDetection = enable;
            return this;
        }

        /// <summary>
        /// Enable/disable regression trials.
        /// Default: true (enabled if data has many unique values).
        /// </summary>
        /// <param name="enable">Whether to enable regression trials.</param>
        /// <returns>The builder instance for chaining.</returns>
        public AutoMLBuilder TryRegression(bool enable = true)
        {
            _tryRegression = enable;
            return this;
        }

        /// <summary>
        /// Enable/disable similarity search trials.
        /// Default: false (computationally expensive).
        /// </summary>
        /// <param name="enable">Whether to enable similarity search trials.</param>
        /// <returns>The builder instance for chaining.</returns>
        public AutoMLBuilder TrySimilaritySearch(bool enable = true)
        {
            _trySimilaritySearch = enable;
            return this;
        }

        /// <summary>
        /// Specify which architectures to test.
        /// Default: All (Quantum, Hybrid, Classical).
        /// </summary>
        /// <param name="architectures">Architectures to test.</param>
        /// <returns>The builder instance for chaining.</returns>
        public AutoMLBuilder WithArchitectures(params SearchArchitecture[] architectures)
        {
            ArgumentNullException.ThrowIfNull(architectures);
            _tryArchitectures = architectures;
            return this;
        }

        /// <summary>
        /// Set maximum number of trials (model configurations to try).
        /// Default: 20
        /// Higher = more thorough search but slower.
        /// </summary>
        /// <param name="maxTrials">Maximum number of trials.</param>
        /// <returns>The builder instance for chaining.</returns>
        public AutoMLBuilder WithMaxTrials(int maxTrials)
        {
            _maxTrials = maxTrials;
            return this;
        }

        /// <summary>
        /// Set maximum time budget in minutes.
        /// Search will stop after this time even if not all trials completed.
        /// Default: No limit.
        /// </summary>
        /// <param name="minutes">Maximum time in minutes.</param>
        /// <returns>The builder instance for chaining.</returns>
        public AutoMLBuilder WithMaxTimeMinutes(int minutes)
        {
            _maxTimeMinutes = minutes;
            return this;
        }

        /// <summary>
        /// Set train/validation split ratio.
        /// Default: 0.2 (20% validation, 80% training).
        /// </summary>
        /// <param name="split">Validation split ratio.</param>
        /// <returns>The builder instance for chaining.</returns>
        public AutoMLBuilder WithValidationSplit(double split)
        {
            _validationSplit = split;
            return this;
        }

        /// <summary>
        /// Specify quantum backend to use.
        /// Default: LocalBackend (simulation).
        /// </summary>
        /// <param name="backend">Quantum backend to use.</param>
        /// <returns>The builder instance for chaining.</returns>
        public AutoMLBuilder WithBackend(IQuantumBackend backend)
        {
            ArgumentNullException.ThrowIfNull(backend);
            _backend = backend;
            return this;
        }

        /// <summary>
        /// Enable verbose logging to see trial progress.
        /// Default: false.
        /// </summary>
        /// <param name="verbose">Whether to enable verbose logging.</param>
        /// <returns>The builder instance for chaining.</returns>
        public AutoMLBuilder WithVerbose(bool verbose = true)
        {
            _verbose = verbose;
            return this;
        }

        /// <summary>
        /// Save best model to specified path.
        /// </summary>
        /// <param name="path">Path to save the best model.</param>
        /// <returns>The builder instance for chaining.</returns>
        public AutoMLBuilder SaveBestModelTo(string path)
        {
            ArgumentNullException.ThrowIfNull(path);
            _savePath = path;
            return this;
        }

        /// <summary>
        /// Set random seed for reproducibility.
        /// </summary>
        /// <param name="seed">Random seed value.</param>
        /// <returns>The builder instance for chaining.</returns>
        public AutoMLBuilder WithRandomSeed(int seed)
        {
            _randomSeed = seed;
            return this;
        }

        /// <summary>
        /// Run AutoML search to find the best model.
        /// This will try multiple model types, architectures, and hyperparameters.
        /// Returns the best model found with a detailed report.
        /// </summary>
        /// <exception cref="InvalidOperationException">Thrown if search fails.</exception>
        /// <returns>An <see cref="IAutoMLResult"/> containing the best model and search results.</returns>
        public IAutoMLResult Build()
        {
            // Build F# problem specification
            var problem = new AutoML.AutoMLProblem(
                _trainFeatures,
                _trainLabels,
                _tryBinaryClassification,
                _tryMultiClass.HasValue ? FSharpOption<int>.Some(_tryMultiClass.Value) : FSharpOption<int>.None,
                _tryAnomalyDetection,
                _tryRegression,
                _trySimilaritySearch,
                ListModule.OfArray(_tryArchitectures.Select(ConvertArchitecture).ToArray()),
                _maxTrials,
                _maxTimeMinutes.HasValue ? FSharpOption<int>.Some(_maxTimeMinutes.Value) : FSharpOption<int>.None,
                _validationSplit,
                _backend != null ? FSharpOption<IQuantumBackend>.Some(_backend) : FSharpOption<IQuantumBackend>.None,
                _verbose,
                FSharpOption<Microsoft.Extensions.Logging.ILogger>.None,
                _savePath != null ? FSharpOption<string>.Some(_savePath) : FSharpOption<string>.None,
                _randomSeed.HasValue ? FSharpOption<int>.Some(_randomSeed.Value) : FSharpOption<int>.None,
                FSharpOption<Core.Progress.IProgressReporter>.None,
                FSharpOption<System.Threading.CancellationToken>.None);

            // Run AutoML search
            var result = AutoML.search(problem);

            if (result.IsError)
            {
                throw new InvalidOperationException($"AutoML search failed: {result.ErrorValue.Message}");
            }

            return new AutoMLResultWrapper(result.ResultValue);
        }

        private static AutoML.Architecture ConvertArchitecture(SearchArchitecture arch)
        {
            return arch switch
            {
                SearchArchitecture.Quantum => AutoML.Architecture.Quantum,
                SearchArchitecture.Hybrid => AutoML.Architecture.Hybrid,
                SearchArchitecture.Classical => AutoML.Architecture.Classical,
                _ => AutoML.Architecture.Quantum,
            };
        }
    }

    /// <summary>
    /// Architecture options for AutoML search.
    /// </summary>
    public enum SearchArchitecture
    {
        /// <summary>Pure quantum model using variational circuits.</summary>
        Quantum,

        /// <summary>Hybrid quantum-classical using quantum kernels.</summary>
        Hybrid,

        /// <summary>Classical baseline for comparison.</summary>
        Classical,
    }

    /// <summary>
    /// AutoML search result with best model and detailed report.
    /// </summary>
    public interface IAutoMLResult
    {
        /// <summary>Gets best model type found (e.g., "Binary Classification", "Regression").</summary>
        string BestModelType { get; }

        /// <summary>Gets best architecture found.</summary>
        SearchArchitecture BestArchitecture { get; }

        /// <summary>Gets validation score of best model (accuracy for classification, R² for regression).</summary>
        double Score { get; }

        /// <summary>Gets all trial results (for analysis).</summary>
        TrialResult[] AllTrials { get; }

        /// <summary>Gets total time spent searching.</summary>
        TimeSpan TotalSearchTime { get; }

        /// <summary>Gets number of successful trials.</summary>
        int SuccessfulTrials { get; }

        /// <summary>Gets number of failed trials.</summary>
        int FailedTrials { get; }

        /// <summary>Gets model metadata.</summary>
        AutoMLMetadata Metadata { get; }

        /// <summary>
        /// Make prediction with the best model.
        /// Returns appropriate prediction type based on model type.
        /// </summary>
        /// <param name="features">Feature vector to predict.</param>
        /// <returns>Prediction result (type depends on best model type).</returns>
        object Predict(double[] features);

        /// <summary>
        /// Gets get best hyperparameters found.
        /// </summary>
        HyperparameterConfig BestHyperparameters { get; }
    }

    /// <summary>
    /// Single trial result from AutoML search.
    /// </summary>
    public class TrialResult
    {
        /// <summary>Gets trial ID.</summary>
        public int Id { get; init; }

        /// <summary>Gets model type tested.</summary>
        public required string ModelType { get; init; }

        /// <summary>Gets architecture used.</summary>
        public SearchArchitecture Architecture { get; init; }

        /// <summary>Gets hyperparameters used.</summary>
        public required HyperparameterConfig Hyperparameters { get; init; }

        /// <summary>Gets validation score achieved.</summary>
        public double Score { get; init; }

        /// <summary>Gets training time.</summary>
        public TimeSpan TrainingTime { get; init; }

        /// <summary>Gets a value indicating whether the trial succeeded.</summary>
        public bool Success { get; init; }

        /// <summary>Gets error message (if failed).</summary>
        public string? ErrorMessage { get; init; }
    }

    /// <summary>
    /// AutoML metadata.
    /// </summary>
    public class AutoMLMetadata
    {
        /// <summary>Gets number of input features.</summary>
        public int NumFeatures { get; init; }

        /// <summary>Gets number of samples used for training/validation.</summary>
        public int NumSamples { get; init; }

        /// <summary>Gets timestamp when the best model was created.</summary>
        public DateTime CreatedAt { get; init; }

        /// <summary>Gets timestamp when the search completed.</summary>
        public DateTime SearchCompleted { get; init; }

        /// <summary>Gets optional user note stored with the best model.</summary>
        public string? Note { get; init; }
    }

    /// <summary>
    /// Anomaly detection result (simplified for C# consumers).
    /// </summary>
    public class AutoMLAnomalyResult
    {
        /// <summary>Gets a value indicating whether the sample is anomalous.</summary>
        public bool IsAnomaly { get; init; }

        /// <summary>Gets anomaly score [0, 1], higher means more anomalous.</summary>
        public double AnomalyScore { get; init; }

        /// <summary>Gets decision threshold; scores above are considered anomalies.</summary>
        public double Threshold { get; init; }
    }

    /// <summary>
    /// Hyperparameter configuration.
    /// </summary>
    public class HyperparameterConfig
    {
        /// <summary>Gets learning rate.</summary>
        public double LearningRate { get; init; }

        /// <summary>Gets maximum training epochs.</summary>
        public int MaxEpochs { get; init; }

        /// <summary>Gets convergence threshold.</summary>
        public double ConvergenceThreshold { get; init; }

        /// <summary>Gets number of measurement shots.</summary>
        public int Shots { get; init; }
    }

    // ============================================================================
    // INTERNAL WRAPPER - Hides F# types from C# consumers
    // ============================================================================
    internal class AutoMLResultWrapper : IAutoMLResult
    {
        private readonly AutoML.AutoMLResult _result;

        public AutoMLResultWrapper(AutoML.AutoMLResult result)
        {
            _result = result;
        }

        public string BestModelType => _result.BestModelType;

        public SearchArchitecture BestArchitecture
        {
            get
            {
                return _result.BestArchitecture.IsQuantum ? SearchArchitecture.Quantum
                    : _result.BestArchitecture.IsHybrid ? SearchArchitecture.Hybrid
                    : SearchArchitecture.Classical;
            }
        }

        public double Score => _result.Score;

        public TrialResult[] AllTrials
        {
            get
            {
                return _result.AllTrials.Select(t => new TrialResult
                {
                    Id = t.Id,
                    ModelType = ConvertModelType(t.ModelType),
                    Architecture = t.Architecture.IsQuantum ? SearchArchitecture.Quantum
                        : t.Architecture.IsHybrid ? SearchArchitecture.Hybrid
                        : SearchArchitecture.Classical,
                    Hyperparameters = new HyperparameterConfig
                    {
                        LearningRate = t.Hyperparameters.LearningRate,
                        MaxEpochs = t.Hyperparameters.MaxEpochs,
                        ConvergenceThreshold = t.Hyperparameters.ConvergenceThreshold,
                        Shots = t.Hyperparameters.Shots,
                    },
                    Score = t.Score,
                    TrainingTime = t.TrainingTime,
                    Success = t.Success,
                    ErrorMessage = FSharpOption<string>.get_IsSome(t.ErrorMessage)
                        ? t.ErrorMessage.Value
                        : null,
                }).ToArray();
            }
        }

        public TimeSpan TotalSearchTime => _result.TotalSearchTime;

        public int SuccessfulTrials => _result.SuccessfulTrials;

        public int FailedTrials => _result.FailedTrials;

        public AutoMLMetadata Metadata
        {
            get
            {
                var meta = _result.Metadata;
                return new AutoMLMetadata
                {
                    NumFeatures = meta.NumFeatures,
                    NumSamples = meta.NumSamples,
                    CreatedAt = meta.CreatedAt,
                    SearchCompleted = meta.SearchCompleted,
                    Note = FSharpOption<string>.get_IsSome(meta.Note) ? meta.Note.Value : null,
                };
            }
        }

        public HyperparameterConfig BestHyperparameters
        {
            get
            {
                var hp = _result.BestHyperparameters;
                return new HyperparameterConfig
                {
                    LearningRate = hp.LearningRate,
                    MaxEpochs = hp.MaxEpochs,
                    ConvergenceThreshold = hp.ConvergenceThreshold,
                    Shots = hp.Shots,
                };
            }
        }

        public object Predict(double[] features)
        {
            var result = AutoML.predict(features, _result);

            if (result.IsError)
            {
                throw new InvalidOperationException($"Prediction failed: {result.ErrorValue.Message}");
            }

            var prediction = result.ResultValue;

            // Unwrap F# discriminated union
            if (prediction.IsBinaryPrediction)
            {
                var binaryPred = ((AutoML.Prediction.BinaryPrediction)prediction).Item;
                return new ClassificationResult
                {
                    Label = binaryPred.Label,
                    Confidence = binaryPred.Confidence,
                    IsPositive = binaryPred.IsPositive,
                    IsNegative = binaryPred.IsNegative,
                };
            }
            else if (prediction.IsCategoryPrediction)
            {
                var categoryPred = ((AutoML.Prediction.CategoryPrediction)prediction).Item;
                return new CategoryPrediction
                {
                    Category = categoryPred.Category,
                    Confidence = categoryPred.Confidence,
                    Probabilities = categoryPred.Probabilities,
                    ModelType = categoryPred.ModelType,
                };
            }
            else if (prediction.IsRegressionPrediction)
            {
                var regressionPred = ((AutoML.Prediction.RegressionPrediction)prediction).Item;

                (double, double)? confidenceInterval = null;
                if (FSharpOption<Tuple<double, double>>.get_IsSome(regressionPred.ConfidenceInterval))
                {
                    var ci = regressionPred.ConfidenceInterval.Value;
                    confidenceInterval = (ci.Item1, ci.Item2);
                }

                return new RegressionPrediction
                {
                    Value = regressionPred.Value,
                    ConfidenceInterval = confidenceInterval,
                    ModelType = regressionPred.ModelType,
                };
            }
            else if (prediction.IsAnomalyPrediction)
            {
                var anomalyPred = ((AutoML.Prediction.AnomalyPrediction)prediction).Item;
                return new AutoMLAnomalyResult
                {
                    IsAnomaly = anomalyPred.IsAnomaly,
                    AnomalyScore = anomalyPred.AnomalyScore,
                    Threshold = anomalyPred.AnomalyScore,
                };
            }
            else
            {
                throw new InvalidOperationException("Unknown prediction type");
            }
        }

        private static string ConvertModelType(AutoML.ModelType modelType)
        {
            if (modelType.IsBinaryClassification)
            {
                return "Binary Classification";
            }
            else if (modelType.IsMultiClassClassification)
            {
                return $"Multi-Class Classification ({((AutoML.ModelType.MultiClassClassification)modelType).Item} classes)";
            }
            else if (modelType.IsRegression)
            {
                return "Regression";
            }
            else if (modelType.IsAnomalyDetection)
            {
                return "Anomaly Detection";
            }
            else if (modelType.IsSimilaritySearch)
            {
                return "Similarity Search";
            }
            else
            {
                return "Unknown";
            }
        }
    }
}
