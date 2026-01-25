using System;
using FSharp.Azure.Quantum.Core;
using Microsoft.FSharp.Core;
using static FSharp.Azure.Quantum.Core.BackendAbstraction;

namespace FSharp.Azure.Quantum.Business.CSharp
{
    /// <summary>
    /// C# Fluent API for Binary Classification
    ///
    /// Provides enterprise-friendly API for binary classification without exposing F# types.
    /// Use this for production .NET applications where you need to classify items into two categories.
    ///
    /// Example:
    /// <code>
    /// var classifier = new BinaryClassificationBuilder()
    ///     .WithFeatures(trainX)
    ///     .WithLabels(trainY)
    ///     .Build();
    ///
    /// var result = classifier.Classify(newSample);
    /// if (result.IsFraud)
    ///     BlockTransaction();
    /// </code>
    /// </summary>
    public class BinaryClassificationBuilder
    {
        private double[][]? _trainFeatures;
        private int[]? _trainLabels;
        private Architecture _architecture = Architecture.Quantum;
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
        /// </summary>
        /// <param name="features">Training features matrix.</param>
        /// <returns>The builder instance for chaining.</returns>
        public BinaryClassificationBuilder WithFeatures(double[][] features)
        {
            ArgumentNullException.ThrowIfNull(features);
            _trainFeatures = features;
            return this;
        }

        /// <summary>
        /// Set training labels (0 or 1 for each sample).
        /// </summary>
        /// <param name="labels">Training labels array.</param>
        /// <returns>The builder instance for chaining.</returns>
        public BinaryClassificationBuilder WithLabels(int[] labels)
        {
            ArgumentNullException.ThrowIfNull(labels);
            _trainLabels = labels;
            return this;
        }

        /// <summary>
        /// Choose classification architecture (Quantum, Hybrid, or Classical).
        /// Default: Quantum.
        /// </summary>
        /// <param name="architecture">Classification architecture to use.</param>
        /// <returns>The builder instance for chaining.</returns>
        public BinaryClassificationBuilder WithArchitecture(Architecture architecture)
        {
            _architecture = architecture;
            return this;
        }

        /// <summary>
        /// Set learning rate for training.
        /// Default: 0.01.
        /// </summary>
        /// <param name="learningRate">Learning rate value.</param>
        /// <returns>The builder instance for chaining.</returns>
        public BinaryClassificationBuilder WithLearningRate(double learningRate)
        {
            _learningRate = learningRate;
            return this;
        }

        /// <summary>
        /// Set maximum number of training epochs.
        /// Default: 100.
        /// </summary>
        /// <param name="maxEpochs">Maximum number of epochs.</param>
        /// <returns>The builder instance for chaining.</returns>
        public BinaryClassificationBuilder WithMaxEpochs(int maxEpochs)
        {
            _maxEpochs = maxEpochs;
            return this;
        }

        /// <summary>
        /// Set convergence threshold for early stopping.
        /// Default: 0.001.
        /// </summary>
        /// <param name="threshold">Convergence threshold value.</param>
        /// <returns>The builder instance for chaining.</returns>
        public BinaryClassificationBuilder WithConvergenceThreshold(double threshold)
        {
            _convergenceThreshold = threshold;
            return this;
        }

        /// <summary>
        /// Specify quantum backend to use.
        /// Default: LocalBackend (simulation).
        /// </summary>
        /// <param name="backend">Quantum backend to use.</param>
        /// <returns>The builder instance for chaining.</returns>
        public BinaryClassificationBuilder WithBackend(IQuantumBackend backend)
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
        /// <returns>The builder instance for chaining.</returns>
        public BinaryClassificationBuilder WithShots(int shots)
        {
            _shots = shots;
            return this;
        }

        /// <summary>
        /// Enable verbose logging during training.
        /// Default: false.
        /// </summary>
        /// <param name="verbose">Whether to enable verbose logging.</param>
        /// <returns>The builder instance for chaining.</returns>
        public BinaryClassificationBuilder WithVerbose(bool verbose = true)
        {
            _verbose = verbose;
            return this;
        }

        /// <summary>
        /// Save trained model to specified path.
        /// </summary>
        /// <param name="path">Path to save the model.</param>
        /// <returns>The builder instance for chaining.</returns>
        public BinaryClassificationBuilder SaveModelTo(string path)
        {
            ArgumentNullException.ThrowIfNull(path);
            _savePath = path;
            return this;
        }

        /// <summary>
        /// Add optional note about the model (saved in metadata).
        /// </summary>
        /// <param name="note">Note to save with the model.</param>
        /// <returns>The builder instance for chaining.</returns>
        public BinaryClassificationBuilder WithNote(string note)
        {
            ArgumentNullException.ThrowIfNull(note);
            _note = note;
            return this;
        }

        /// <summary>
        /// Build and train the classifier.
        /// Returns a trained classifier ready for predictions.
        /// </summary>
        /// <exception cref="InvalidOperationException">Thrown if training fails.</exception>
        /// <returns>A trained <see cref="IBinaryClassifier"/> instance.</returns>
        public IBinaryClassifier Build()
        {
            // Build F# problem specification
            var problem = new BinaryClassifier.ClassificationProblem(
                _trainFeatures ?? throw new InvalidOperationException("Training features are required"),
                _trainLabels ?? throw new InvalidOperationException("Training labels are required"),
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

            // Train classifier
            var result = BinaryClassifier.train(problem);

            if (result.IsError)
            {
                throw new InvalidOperationException($"Training failed: {result.ErrorValue.Message}");
            }

            return new BinaryClassifierWrapper(result.ResultValue);
        }

        /// <summary>
        /// Load a previously trained classifier from file.
        /// </summary>
        /// <param name="path">Path to the saved classifier file.</param>
        /// <returns>A loaded <see cref="IBinaryClassifier"/> instance.</returns>
        public static IBinaryClassifier LoadFrom(string path)
        {
            ArgumentNullException.ThrowIfNull(path);
            var result = BinaryClassifier.load(path);

            if (result.IsError)
            {
                throw new InvalidOperationException($"Failed to load model: {result.ErrorValue.Message}");
            }

            return new BinaryClassifierWrapper(result.ResultValue);
        }

        private static BinaryClassifier.Architecture ConvertArchitecture(Architecture arch)
        {
            return arch switch
            {
                Architecture.Quantum => BinaryClassifier.Architecture.Quantum,
                Architecture.Hybrid => BinaryClassifier.Architecture.Hybrid,
                Architecture.Classical => BinaryClassifier.Architecture.Classical,
                _ => BinaryClassifier.Architecture.Quantum,
            };
        }
    }

    /// <summary>
    /// Architecture choice for binary classification.
    /// </summary>
    public enum Architecture
    {
        /// <summary>Pure quantum classifier using variational quantum circuits.</summary>
        Quantum,

        /// <summary>Hybrid quantum-classical using quantum kernel SVM.</summary>
        Hybrid,

        /// <summary>Classical baseline for comparison.</summary>
        Classical,
    }

    /// <summary>
    /// Interface for trained binary classifier.
    /// Provides simple API for predictions without F# types.
    /// </summary>
    public interface IBinaryClassifier
    {
        /// <summary>
        /// Classify a new sample.
        /// </summary>
        /// <param name="sample">Feature vector to classify.</param>
        /// <returns>Prediction result with label and confidence.</returns>
        ClassificationResult Classify(double[] sample);

        /// <summary>
        /// Evaluate classifier on test set.
        /// </summary>
        /// <param name="testFeatures">Test samples.</param>
        /// <param name="testLabels">True labels.</param>
        /// <returns>Evaluation metrics.</returns>
        EvaluationMetrics Evaluate(double[][] testFeatures, int[] testLabels);

        /// <summary>
        /// Save classifier to file.
        /// </summary>
        /// <param name="path">Path to save the classifier.</param>
        void SaveTo(string path);

        /// <summary>
        /// Gets get classifier metadata.
        /// </summary>
        ClassifierMetadata Metadata { get; }
    }

    /// <summary>
    /// Classification result (no F# types exposed).
    /// </summary>
    public class ClassificationResult
    {
        /// <summary>Gets predicted class (0 or 1).</summary>
        public int Label { get; init; }

        /// <summary>Gets confidence score [0, 1].</summary>
        public double Confidence { get; init; }

        /// <summary>Gets a value indicating whether true if predicted class is 1 (positive/fraud/spam/etc).</summary>
        public bool IsPositive { get; init; }

        /// <summary>Gets a value indicating whether true if predicted class is 0 (negative/legitimate/ham/etc).</summary>
        public bool IsNegative { get; init; }

        /// <summary>
        /// Gets a value indicating whether convenience property for fraud detection use case.
        /// Same as IsPositive.
        /// </summary>
        public bool IsFraud => IsPositive;

        /// <summary>
        /// Gets a value indicating whether convenience property for spam filtering use case.
        /// Same as IsPositive.
        /// </summary>
        public bool IsSpam => IsPositive;

        /// <summary>
        /// Gets a value indicating whether convenience property for churn prediction use case.
        /// Same as IsPositive.
        /// </summary>
        public bool WillChurn => IsPositive;
    }

    /// <summary>
    /// Evaluation metrics (no F# types exposed).
    /// </summary>
    public class EvaluationMetrics
    {
        /// <summary>Gets overall accuracy [0, 1].</summary>
        public double Accuracy { get; init; }

        /// <summary>Gets precision [0, 1].</summary>
        public double Precision { get; init; }

        /// <summary>Gets recall [0, 1].</summary>
        public double Recall { get; init; }

        /// <summary>Gets f1 score [0, 1].</summary>
        public double F1Score { get; init; }

        /// <summary>Gets true positive count.</summary>
        public int TruePositives { get; init; }

        /// <summary>Gets true negative count.</summary>
        public int TrueNegatives { get; init; }

        /// <summary>Gets false positive count.</summary>
        public int FalsePositives { get; init; }

        /// <summary>Gets false negative count.</summary>
        public int FalseNegatives { get; init; }
    }

    /// <summary>
    /// Classifier metadata (no F# types exposed).
    /// </summary>
    public class ClassifierMetadata
    {
        /// <summary>Gets architecture used for training.</summary>
        public Architecture Architecture { get; init; }

        /// <summary>Gets training accuracy [0, 1].</summary>
        public double TrainingAccuracy { get; init; }

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

    // ============================================================================
    // INTERNAL WRAPPER - Hides F# types from C# consumers
    // ============================================================================
    internal class BinaryClassifierWrapper : IBinaryClassifier
    {
        private readonly BinaryClassifier.Classifier _classifier;

        public BinaryClassifierWrapper(BinaryClassifier.Classifier classifier)
        {
            _classifier = classifier;
        }

        public ClassificationResult Classify(double[] sample)
        {
            var result = BinaryClassifier.predict(sample, _classifier);

            if (result.IsError)
            {
                throw new InvalidOperationException($"Prediction failed: {result.ErrorValue.Message}");
            }

            var prediction = result.ResultValue;

            return new ClassificationResult
            {
                Label = prediction.Label,
                Confidence = prediction.Confidence,
                IsPositive = prediction.IsPositive,
                IsNegative = prediction.IsNegative,
            };
        }

        public EvaluationMetrics Evaluate(double[][] testFeatures, int[] testLabels)
        {
            var result = BinaryClassifier.evaluate(testFeatures, testLabels, _classifier);

            if (result.IsError)
            {
                throw new InvalidOperationException($"Evaluation failed: {result.ErrorValue.Message}");
            }

            var metrics = result.ResultValue;

            return new EvaluationMetrics
            {
                Accuracy = metrics.Accuracy,
                Precision = metrics.Precision,
                Recall = metrics.Recall,
                F1Score = metrics.F1Score,
                TruePositives = metrics.TruePositives,
                TrueNegatives = metrics.TrueNegatives,
                FalsePositives = metrics.FalsePositives,
                FalseNegatives = metrics.FalseNegatives,
            };
        }

        public void SaveTo(string path)
        {
            var result = BinaryClassifier.save(path, _classifier);

            if (result.IsError)
            {
                throw new InvalidOperationException($"Failed to save model: {result.ErrorValue.Message}");
            }
        }

        public ClassifierMetadata Metadata
        {
            get
            {
                var metadata = _classifier.Metadata;
                var arch = metadata.Architecture.IsQuantum ? Architecture.Quantum
                         : metadata.Architecture.IsHybrid ? Architecture.Hybrid
                         : Architecture.Classical;

                return new ClassifierMetadata
                {
                    Architecture = arch,
                    TrainingAccuracy = metadata.TrainingAccuracy,
                    TrainingTime = metadata.TrainingTime,
                    NumFeatures = metadata.NumFeatures,
                    NumSamples = metadata.NumSamples,
                    CreatedAt = metadata.CreatedAt,
                    Note = FSharpOption<string>.get_IsSome(metadata.Note) ? metadata.Note.Value : null,
                };
            }
        }
    }
}
