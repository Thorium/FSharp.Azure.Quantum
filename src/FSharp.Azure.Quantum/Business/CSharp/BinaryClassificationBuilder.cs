using System;
using System.Linq;
using FSharp.Azure.Quantum.Core.BackendAbstraction;
using Microsoft.FSharp.Core;

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
        private double[][] _trainFeatures;
        private int[] _trainLabels;
        private Architecture _architecture = Architecture.Quantum;
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
        /// </summary>
        public BinaryClassificationBuilder WithFeatures(double[][] features)
        {
            _trainFeatures = features;
            return this;
        }

        /// <summary>
        /// Set training labels (0 or 1 for each sample).
        /// </summary>
        public BinaryClassificationBuilder WithLabels(int[] labels)
        {
            _trainLabels = labels;
            return this;
        }

        /// <summary>
        /// Choose classification architecture (Quantum, Hybrid, or Classical).
        /// Default: Quantum
        /// </summary>
        public BinaryClassificationBuilder WithArchitecture(Architecture architecture)
        {
            _architecture = architecture;
            return this;
        }

        /// <summary>
        /// Set learning rate for training.
        /// Default: 0.01
        /// </summary>
        public BinaryClassificationBuilder WithLearningRate(double learningRate)
        {
            _learningRate = learningRate;
            return this;
        }

        /// <summary>
        /// Set maximum number of training epochs.
        /// Default: 100
        /// </summary>
        public BinaryClassificationBuilder WithMaxEpochs(int maxEpochs)
        {
            _maxEpochs = maxEpochs;
            return this;
        }

        /// <summary>
        /// Set convergence threshold for early stopping.
        /// Default: 0.001
        /// </summary>
        public BinaryClassificationBuilder WithConvergenceThreshold(double threshold)
        {
            _convergenceThreshold = threshold;
            return this;
        }

        /// <summary>
        /// Specify quantum backend to use.
        /// Default: LocalBackend (simulation)
        /// </summary>
        public BinaryClassificationBuilder WithBackend(IQuantumBackend backend)
        {
            _backend = backend;
            return this;
        }

        /// <summary>
        /// Set number of measurement shots for quantum circuits.
        /// Default: 1000
        /// </summary>
        public BinaryClassificationBuilder WithShots(int shots)
        {
            _shots = shots;
            return this;
        }

        /// <summary>
        /// Enable verbose logging during training.
        /// Default: false
        /// </summary>
        public BinaryClassificationBuilder WithVerbose(bool verbose = true)
        {
            _verbose = verbose;
            return this;
        }

        /// <summary>
        /// Save trained model to specified path.
        /// </summary>
        public BinaryClassificationBuilder SaveModelTo(string path)
        {
            _savePath = path;
            return this;
        }

        /// <summary>
        /// Add optional note about the model (saved in metadata).
        /// </summary>
        public BinaryClassificationBuilder WithNote(string note)
        {
            _note = note;
            return this;
        }

        /// <summary>
        /// Build and train the classifier.
        /// Returns a trained classifier ready for predictions.
        /// </summary>
        /// <exception cref="InvalidOperationException">Thrown if training fails.</exception>
        public IBinaryClassifier Build()
        {
            // Build F# problem specification
            var problem = new BinaryClassifier.ClassificationProblem(
                TrainFeatures: _trainFeatures,
                TrainLabels: _trainLabels,
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

            // Train classifier
            var result = BinaryClassifier.train(problem);

            if (FSharpResult<BinaryClassifier.Classifier, string>.get_IsError(result))
            {
                var error = ((FSharpResult<BinaryClassifier.Classifier, string>.Error)result).ErrorValue;
                throw new InvalidOperationException($"Training failed: {error}");
            }

            var classifier = ((FSharpResult<BinaryClassifier.Classifier, string>.Ok)result).ResultValue;
            return new BinaryClassifierWrapper(classifier);
        }

        /// <summary>
        /// Load a previously trained classifier from file.
        /// </summary>
        public static IBinaryClassifier LoadFrom(string path)
        {
            var result = BinaryClassifier.load(path);

            if (FSharpResult<BinaryClassifier.Classifier, string>.get_IsError(result))
            {
                var error = ((FSharpResult<BinaryClassifier.Classifier, string>.Error)result).ErrorValue;
                throw new InvalidOperationException($"Failed to load model: {error}");
            }

            var classifier = ((FSharpResult<BinaryClassifier.Classifier, string>.Ok)result).ResultValue;
            return new BinaryClassifierWrapper(classifier);
        }

        private static BinaryClassifier.Architecture ConvertArchitecture(Architecture arch)
        {
            return arch switch
            {
                Architecture.Quantum => BinaryClassifier.Architecture.Quantum,
                Architecture.Hybrid => BinaryClassifier.Architecture.Hybrid,
                Architecture.Classical => BinaryClassifier.Architecture.Classical,
                _ => BinaryClassifier.Architecture.Quantum
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
        Classical
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
        void SaveTo(string path);

        /// <summary>
        /// Get classifier metadata.
        /// </summary>
        ClassifierMetadata Metadata { get; }
    }

    /// <summary>
    /// Classification result (no F# types exposed).
    /// </summary>
    public class ClassificationResult
    {
        /// <summary>Predicted class (0 or 1).</summary>
        public int Label { get; init; }

        /// <summary>Confidence score [0, 1].</summary>
        public double Confidence { get; init; }

        /// <summary>True if predicted class is 1 (positive/fraud/spam/etc).</summary>
        public bool IsPositive { get; init; }

        /// <summary>True if predicted class is 0 (negative/legitimate/ham/etc).</summary>
        public bool IsNegative { get; init; }

        /// <summary>
        /// Convenience property for fraud detection use case.
        /// Same as IsPositive.
        /// </summary>
        public bool IsFraud => IsPositive;

        /// <summary>
        /// Convenience property for spam filtering use case.
        /// Same as IsPositive.
        /// </summary>
        public bool IsSpam => IsPositive;

        /// <summary>
        /// Convenience property for churn prediction use case.
        /// Same as IsPositive.
        /// </summary>
        public bool WillChurn => IsPositive;
    }

    /// <summary>
    /// Evaluation metrics (no F# types exposed).
    /// </summary>
    public class EvaluationMetrics
    {
        public double Accuracy { get; init; }
        public double Precision { get; init; }
        public double Recall { get; init; }
        public double F1Score { get; init; }
        public int TruePositives { get; init; }
        public int TrueNegatives { get; init; }
        public int FalsePositives { get; init; }
        public int FalseNegatives { get; init; }
    }

    /// <summary>
    /// Classifier metadata (no F# types exposed).
    /// </summary>
    public class ClassifierMetadata
    {
        public Architecture Architecture { get; init; }
        public double TrainingAccuracy { get; init; }
        public TimeSpan TrainingTime { get; init; }
        public int NumFeatures { get; init; }
        public int NumSamples { get; init; }
        public DateTime CreatedAt { get; init; }
        public string Note { get; init; }
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

            if (FSharpResult<BinaryClassifier.Prediction, string>.get_IsError(result))
            {
                var error = ((FSharpResult<BinaryClassifier.Prediction, string>.Error)result).ErrorValue;
                throw new InvalidOperationException($"Prediction failed: {error}");
            }

            var prediction = ((FSharpResult<BinaryClassifier.Prediction, string>.Ok)result).ResultValue;

            return new ClassificationResult
            {
                Label = prediction.Label,
                Confidence = prediction.Confidence,
                IsPositive = prediction.IsPositive,
                IsNegative = prediction.IsNegative
            };
        }

        public EvaluationMetrics Evaluate(double[][] testFeatures, int[] testLabels)
        {
            var result = BinaryClassifier.evaluate(testFeatures, testLabels, _classifier);

            if (FSharpResult<BinaryClassifier.EvaluationMetrics, string>.get_IsError(result))
            {
                var error = ((FSharpResult<BinaryClassifier.EvaluationMetrics, string>.Error)result).ErrorValue;
                throw new InvalidOperationException($"Evaluation failed: {error}");
            }

            var metrics = ((FSharpResult<BinaryClassifier.EvaluationMetrics, string>.Ok)result).ResultValue;

            return new EvaluationMetrics
            {
                Accuracy = metrics.Accuracy,
                Precision = metrics.Precision,
                Recall = metrics.Recall,
                F1Score = metrics.F1Score,
                TruePositives = metrics.TruePositives,
                TrueNegatives = metrics.TrueNegatives,
                FalsePositives = metrics.FalsePositives,
                FalseNegatives = metrics.FalseNegatives
            };
        }

        public void SaveTo(string path)
        {
            var result = BinaryClassifier.save(path, _classifier);

            if (FSharpResult<Microsoft.FSharp.Core.Unit, string>.get_IsError(result))
            {
                var error = ((FSharpResult<Microsoft.FSharp.Core.Unit, string>.Error)result).ErrorValue;
                throw new InvalidOperationException($"Failed to save model: {error}");
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
                    Note = FSharpOption<string>.get_IsSome(metadata.Note) ? metadata.Note.Value : null
                };
            }
        }
    }
}
