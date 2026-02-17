using System;
using System.Linq;
using FSharp.Azure.Quantum.Core;
using Microsoft.FSharp.Core;
using static FSharp.Azure.Quantum.Core.BackendAbstraction;

namespace FSharp.Azure.Quantum.Business.CSharp
{
    /// <summary>
    /// C# Fluent API for Anomaly Detection
    ///
    /// Detects unusual patterns by learning from normal data only.
    /// Use this when you have examples of normal behavior and want to find outliers.
    ///
    /// Example:
    /// <code>
    /// var result = detector.Check(suspiciousTransaction);
    /// if (result.IsAnomaly &amp;&amp; result.AnomalyScore > 0.8)
    ///     BlockTransaction();
    /// </code>
    /// </summary>
    public class AnomalyDetectionBuilder
    {
        private double[][]? _normalData;
        private Sensitivity _sensitivity = Sensitivity.Medium;
        private double _contaminationRate = 0.05;
        private IQuantumBackend? _backend;
        private int _shots = 1000;
        private bool _verbose;
        private string? _savePath;
        private string? _note;

        /// <summary>
        /// Set training data (normal examples only).
        /// The detector will learn what "normal" looks like from this data.
        /// </summary>
        /// <param name="normalData">Training data containing normal examples.</param>
        /// <returns>The builder instance for chaining.</returns>
        public AnomalyDetectionBuilder TrainOnNormalData(double[][] normalData)
        {
            ArgumentNullException.ThrowIfNull(normalData);
            _normalData = normalData;
            return this;
        }

        /// <summary>
        /// Set sensitivity level for detection.
        /// Low: Fewer false alarms, may miss some anomalies
        /// Medium: Balanced (default)
        /// High: More sensitive, more false alarms
        /// VeryHigh: Maximum sensitivity.
        /// </summary>
        /// <param name="sensitivity">Sensitivity level for detection.</param>
        /// <returns>The builder instance for chaining.</returns>
        public AnomalyDetectionBuilder WithSensitivity(Sensitivity sensitivity)
        {
            _sensitivity = sensitivity;
            return this;
        }

        /// <summary>
        /// Set expected contamination rate in training data (0.0 to 0.5).
        /// If you know some training data may contain anomalies, set this.
        /// Default: 0.05 (5%).
        /// </summary>
        /// <param name="rate">Contamination rate value.</param>
        /// <returns>The builder instance for chaining.</returns>
        public AnomalyDetectionBuilder WithContaminationRate(double rate)
        {
            _contaminationRate = rate;
            return this;
        }

        /// <summary>
        /// Specify quantum backend to use.
        /// Default: LocalBackend (simulation).
        /// </summary>
        /// <param name="backend">Quantum backend to use.</param>
        /// <returns>The builder instance for chaining.</returns>
        public AnomalyDetectionBuilder WithBackend(IQuantumBackend backend)
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
        public AnomalyDetectionBuilder WithShots(int shots)
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
        public AnomalyDetectionBuilder WithVerbose(bool verbose = true)
        {
            _verbose = verbose;
            return this;
        }

        /// <summary>
        /// Save trained model to specified path.
        /// </summary>
        /// <param name="path">Path to save the model.</param>
        /// <returns>The builder instance for chaining.</returns>
        public AnomalyDetectionBuilder SaveModelTo(string path)
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
        public AnomalyDetectionBuilder WithNote(string note)
        {
            ArgumentNullException.ThrowIfNull(note);
            _note = note;
            return this;
        }

        /// <summary>
        /// Build and train the anomaly detector.
        /// Returns a trained detector ready to check for anomalies.
        /// </summary>
        /// <exception cref="InvalidOperationException">Thrown if training fails.</exception>
        /// <returns>A trained <see cref="IAnomalyDetector"/> instance.</returns>
        public IAnomalyDetector Build()
        {
            // Build F# problem specification
            var problem = new AnomalyDetector.DetectionProblem(
                _normalData ?? throw new InvalidOperationException("Normal training data is required"),
                ConvertSensitivity(_sensitivity),
                _contaminationRate,
                _backend != null ? FSharpOption<IQuantumBackend>.Some(_backend) : FSharpOption<IQuantumBackend>.None,
                _shots,
                _verbose,
                FSharpOption<Microsoft.Extensions.Logging.ILogger>.None,
                _savePath != null ? FSharpOption<string>.Some(_savePath) : FSharpOption<string>.None,
                _note != null ? FSharpOption<string>.Some(_note) : FSharpOption<string>.None,
                FSharpOption<Core.Progress.IProgressReporter>.None,
                FSharpOption<System.Threading.CancellationToken>.None);

            // Train detector
            var result = AnomalyDetector.train(problem);

            if (result.IsError)
            {
                throw new InvalidOperationException($"Training failed: {result.ErrorValue.Message}");
            }

            return new AnomalyDetectorWrapper(result.ResultValue);
        }

        /// <summary>
        /// Load a previously trained detector from file.
        /// </summary>
        /// <param name="path">Path to the saved detector file.</param>
        /// <returns>A loaded <see cref="IAnomalyDetector"/> instance.</returns>
        public static IAnomalyDetector LoadFrom(string path)
        {
            ArgumentNullException.ThrowIfNull(path);
            var result = AnomalyDetector.load(path);

            if (result.IsError)
            {
                throw new InvalidOperationException($"Failed to load detector: {result.ErrorValue.Message}");
            }

            return new AnomalyDetectorWrapper(result.ResultValue);
        }

        private static AnomalyDetector.Sensitivity ConvertSensitivity(Sensitivity sens)
        {
            return sens switch
            {
                Sensitivity.Low => AnomalyDetector.Sensitivity.Low,
                Sensitivity.Medium => AnomalyDetector.Sensitivity.Medium,
                Sensitivity.High => AnomalyDetector.Sensitivity.High,
                Sensitivity.VeryHigh => AnomalyDetector.Sensitivity.VeryHigh,
                _ => AnomalyDetector.Sensitivity.Medium,
            };
        }
    }

    /// <summary>
    /// Sensitivity level for anomaly detection.
    /// </summary>
    public enum Sensitivity
    {
        /// <summary>Conservative - fewer false alarms, may miss some anomalies.</summary>
        Low,

        /// <summary>Balanced - good trade-off between detection and false alarms.</summary>
        Medium,

        /// <summary>Aggressive - more detections, more false alarms.</summary>
        High,

        /// <summary>Maximum sensitivity - catches most anomalies but many false alarms.</summary>
        VeryHigh,
    }

    /// <summary>
    /// Interface for trained anomaly detector.
    /// </summary>
    public interface IAnomalyDetector
    {
        /// <summary>
        /// Check if a sample is anomalous.
        /// </summary>
        /// <param name="sample">Feature vector to check.</param>
        /// <returns>Anomaly detection result.</returns>
        AnomalyResult Check(double[] sample);

        /// <summary>
        /// Check multiple samples for anomalies.
        /// </summary>
        /// <param name="samples">Samples to check.</param>
        /// <returns>Batch detection results.</returns>
        BatchResults CheckBatch(double[][] samples);

        /// <summary>
        /// Explain why a sample is anomalous.
        /// Returns features contributing most to anomaly score.
        /// </summary>
        /// <param name="sample">Sample to explain.</param>
        /// <param name="trainingData">Original training data for comparison.</param>
        /// <returns>Feature contributions sorted by importance.</returns>
        FeatureContribution[] Explain(double[] sample, double[][] trainingData);

        /// <summary>
        /// Save detector to file.
        /// </summary>
        /// <param name="path">Path to save the detector.</param>
        void SaveTo(string path);

        /// <summary>
        /// Gets get detector metadata.
        /// </summary>
        DetectorMetadata Metadata { get; }
    }

    /// <summary>
    /// Anomaly detection result (no F# types exposed).
    /// </summary>
    public class AnomalyResult
    {
        /// <summary>Gets a value indicating whether true if sample is anomalous.</summary>
        public bool IsAnomaly { get; init; }

        /// <summary>Gets a value indicating whether true if sample is normal.</summary>
        public bool IsNormal { get; init; }

        /// <summary>Gets anomaly score [0, 1] - higher means more anomalous.</summary>
        public double AnomalyScore { get; init; }

        /// <summary>Gets confidence in the detection [0, 1].</summary>
        public double Confidence { get; init; }

        /// <summary>
        /// Gets recommended action based on score and confidence.
        /// </summary>
        public string Recommendation =>
            IsAnomaly && AnomalyScore > 0.8 ? "BLOCK" :
            IsAnomaly && AnomalyScore > 0.5 ? "REVIEW" :
            "ALLOW";
    }

    /// <summary>
    /// Batch detection results (no F# types exposed).
    /// </summary>
    public class BatchResults
    {
        /// <summary>Gets total items checked.</summary>
        public int TotalItems { get; init; }

        /// <summary>Gets number of anomalies detected.</summary>
        public int AnomaliesDetected { get; init; }

        /// <summary>Gets percentage of anomalies.</summary>
        public double AnomalyRate { get; init; }

        /// <summary>Gets individual results for each sample.</summary>
        public required AnomalyResult[] Results { get; init; }

        /// <summary>Gets indices and scores of top anomalies.</summary>
        public required (int Index, double Score)[] TopAnomalies { get; init; }
    }

    /// <summary>
    /// Feature contribution to anomaly score.
    /// </summary>
    public class FeatureContribution
    {
        /// <summary>Gets feature name or index.</summary>
        public required string FeatureName { get; init; }

        /// <summary>Gets contribution score (higher = more unusual).</summary>
        public double Contribution { get; init; }
    }

    /// <summary>
    /// Detector metadata (no F# types exposed).
    /// </summary>
    public class DetectorMetadata
    {
        /// <summary>Gets sensitivity level used for training.</summary>
        public Sensitivity Sensitivity { get; init; }

        /// <summary>Gets training duration.</summary>
        public TimeSpan TrainingTime { get; init; }

        /// <summary>Gets number of features in the training data.</summary>
        public int NumFeatures { get; init; }

        /// <summary>Gets number of samples used for training.</summary>
        public int NumNormalSamples { get; init; }

        /// <summary>Gets timestamp when the model was created.</summary>
        public DateTime CreatedAt { get; init; }

        /// <summary>Gets optional user note stored with the model.</summary>
        public string? Note { get; init; }
    }

    // ============================================================================
    // INTERNAL WRAPPER - Hides F# types from C# consumers
    // ============================================================================
    internal class AnomalyDetectorWrapper : IAnomalyDetector
    {
        private readonly AnomalyDetector.Detector _detector;

        public AnomalyDetectorWrapper(AnomalyDetector.Detector detector)
        {
            _detector = detector;
        }

        public AnomalyResult Check(double[] sample)
        {
            var result = AnomalyDetector.check(sample, _detector);

            if (result.IsError)
            {
                throw new InvalidOperationException($"Detection failed: {result.ErrorValue.Message}");
            }

            var anomalyResult = result.ResultValue;

            return new AnomalyResult
            {
                IsAnomaly = anomalyResult.IsAnomaly,
                IsNormal = anomalyResult.IsNormal,
                AnomalyScore = anomalyResult.AnomalyScore,
                Confidence = anomalyResult.Confidence,
            };
        }

        public BatchResults CheckBatch(double[][] samples)
        {
            var result = AnomalyDetector.checkBatch(samples, _detector);

            if (result.IsError)
            {
                throw new InvalidOperationException($"Batch detection failed: {result.ErrorValue.Message}");
            }

            var batchResults = result.ResultValue;

            return new BatchResults
            {
                TotalItems = batchResults.TotalItems,
                AnomaliesDetected = batchResults.AnomaliesDetected,
                AnomalyRate = batchResults.AnomalyRate,
                Results = batchResults.Results.Select(r => new AnomalyResult
                {
                    IsAnomaly = r.IsAnomaly,
                    IsNormal = r.IsNormal,
                    AnomalyScore = r.AnomalyScore,
                    Confidence = r.Confidence,
                }).ToArray(),
                TopAnomalies = batchResults.TopAnomalies.Select(t => (t.Item1, t.Item2)).ToArray(),
            };
        }

        public FeatureContribution[] Explain(double[] sample, double[][] trainingData)
        {
            var result = AnomalyDetector.explain(sample, _detector, trainingData);

            if (result.IsError)
            {
                throw new InvalidOperationException($"Explanation failed: {result.ErrorValue.Message}");
            }

            var contributions = result.ResultValue;

            return contributions.Select(c => new FeatureContribution
            {
                FeatureName = c.Item1,
                Contribution = c.Item2,
            }).ToArray();
        }

        public void SaveTo(string path)
        {
            var result = AnomalyDetector.save(path, _detector);

            if (result.IsError)
            {
                throw new InvalidOperationException($"Failed to save detector: {result.ErrorValue.Message}");
            }
        }

        public DetectorMetadata Metadata
        {
            get
            {
                var metadata = _detector.Metadata;
                var sens = metadata.Sensitivity.IsLow ? Sensitivity.Low
                         : metadata.Sensitivity.IsMedium ? Sensitivity.Medium
                         : metadata.Sensitivity.IsHigh ? Sensitivity.High
                         : Sensitivity.VeryHigh;

                return new DetectorMetadata
                {
                    Sensitivity = sens,
                    TrainingTime = metadata.TrainingTime,
                    NumFeatures = metadata.NumFeatures,
                    NumNormalSamples = metadata.NumNormalSamples,
                    CreatedAt = metadata.CreatedAt,
                    Note = FSharpOption<string>.get_IsSome(metadata.Note) ? metadata.Note.Value : null,
                };
            }
        }
    }
}
