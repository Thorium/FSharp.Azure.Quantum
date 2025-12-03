using System;
using System.Linq;
using FSharp.Azure.Quantum.Core.BackendAbstraction;
using Microsoft.FSharp.Core;

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
    /// var detector = new AnomalyDetectionBuilder()
    ///     .TrainOnNormalData(normalTransactions)
    ///     .WithSensitivity(Sensitivity.Medium)
    ///     .Build();
    ///     
    /// var result = detector.Check(suspiciousTransaction);
    /// if (result.IsAnomaly && result.AnomalyScore > 0.8)
    ///     BlockTransaction();
    /// </code>
    /// </summary>
    public class AnomalyDetectionBuilder
    {
        private double[][] _normalData;
        private Sensitivity _sensitivity = Sensitivity.Medium;
        private double _contaminationRate = 0.05;
        private IQuantumBackend _backend = null;
        private int _shots = 1000;
        private bool _verbose = false;
        private string _savePath = null;
        private string _note = null;

        /// <summary>
        /// Set training data (normal examples only).
        /// The detector will learn what "normal" looks like from this data.
        /// </summary>
        public AnomalyDetectionBuilder TrainOnNormalData(double[][] normalData)
        {
            _normalData = normalData;
            return this;
        }

        /// <summary>
        /// Set sensitivity level for detection.
        /// Low: Fewer false alarms, may miss some anomalies
        /// Medium: Balanced (default)
        /// High: More sensitive, more false alarms
        /// VeryHigh: Maximum sensitivity
        /// </summary>
        public AnomalyDetectionBuilder WithSensitivity(Sensitivity sensitivity)
        {
            _sensitivity = sensitivity;
            return this;
        }

        /// <summary>
        /// Set expected contamination rate in training data (0.0 to 0.5).
        /// If you know some training data may contain anomalies, set this.
        /// Default: 0.05 (5%)
        /// </summary>
        public AnomalyDetectionBuilder WithContaminationRate(double rate)
        {
            _contaminationRate = rate;
            return this;
        }

        /// <summary>
        /// Specify quantum backend to use.
        /// Default: LocalBackend (simulation)
        /// </summary>
        public AnomalyDetectionBuilder WithBackend(IQuantumBackend backend)
        {
            _backend = backend;
            return this;
        }

        /// <summary>
        /// Set number of measurement shots for quantum circuits.
        /// Default: 1000
        /// </summary>
        public AnomalyDetectionBuilder WithShots(int shots)
        {
            _shots = shots;
            return this;
        }

        /// <summary>
        /// Enable verbose logging during training.
        /// Default: false
        /// </summary>
        public AnomalyDetectionBuilder WithVerbose(bool verbose = true)
        {
            _verbose = verbose;
            return this;
        }

        /// <summary>
        /// Save trained model to specified path.
        /// </summary>
        public AnomalyDetectionBuilder SaveModelTo(string path)
        {
            _savePath = path;
            return this;
        }

        /// <summary>
        /// Add optional note about the model (saved in metadata).
        /// </summary>
        public AnomalyDetectionBuilder WithNote(string note)
        {
            _note = note;
            return this;
        }

        /// <summary>
        /// Build and train the anomaly detector.
        /// Returns a trained detector ready to check for anomalies.
        /// </summary>
        /// <exception cref="InvalidOperationException">Thrown if training fails.</exception>
        public IAnomalyDetector Build()
        {
            // Build F# problem specification
            var problem = new AnomalyDetector.DetectionProblem(
                NormalData: _normalData,
                Sensitivity: ConvertSensitivity(_sensitivity),
                ContaminationRate: _contaminationRate,
                Backend: _backend != null ? FSharpOption<IQuantumBackend>.Some(_backend) : FSharpOption<IQuantumBackend>.None,
                Shots: _shots,
                Verbose: _verbose,
                SavePath: _savePath != null ? FSharpOption<string>.Some(_savePath) : FSharpOption<string>.None,
                Note: _note != null ? FSharpOption<string>.Some(_note) : FSharpOption<string>.None
            );

            // Train detector
            var result = AnomalyDetector.train(problem);

            if (FSharpResult<AnomalyDetector.Detector, string>.get_IsError(result))
            {
                var error = ((FSharpResult<AnomalyDetector.Detector, string>.Error)result).ErrorValue;
                throw new InvalidOperationException($"Training failed: {error}");
            }

            var detector = ((FSharpResult<AnomalyDetector.Detector, string>.Ok)result).ResultValue;
            return new AnomalyDetectorWrapper(detector);
        }

        /// <summary>
        /// Load a previously trained detector from file.
        /// </summary>
        public static IAnomalyDetector LoadFrom(string path)
        {
            var result = AnomalyDetector.load(path);

            if (FSharpResult<AnomalyDetector.Detector, string>.get_IsError(result))
            {
                var error = ((FSharpResult<AnomalyDetector.Detector, string>.Error)result).ErrorValue;
                throw new InvalidOperationException($"Failed to load detector: {error}");
            }

            var detector = ((FSharpResult<AnomalyDetector.Detector, string>.Ok)result).ResultValue;
            return new AnomalyDetectorWrapper(detector);
        }

        private static AnomalyDetector.Sensitivity ConvertSensitivity(Sensitivity sens)
        {
            return sens switch
            {
                Sensitivity.Low => AnomalyDetector.Sensitivity.Low,
                Sensitivity.Medium => AnomalyDetector.Sensitivity.Medium,
                Sensitivity.High => AnomalyDetector.Sensitivity.High,
                Sensitivity.VeryHigh => AnomalyDetector.Sensitivity.VeryHigh,
                _ => AnomalyDetector.Sensitivity.Medium
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
        VeryHigh
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
        void SaveTo(string path);

        /// <summary>
        /// Get detector metadata.
        /// </summary>
        DetectorMetadata Metadata { get; }
    }

    /// <summary>
    /// Anomaly detection result (no F# types exposed).
    /// </summary>
    public class AnomalyResult
    {
        /// <summary>True if sample is anomalous.</summary>
        public bool IsAnomaly { get; init; }

        /// <summary>True if sample is normal.</summary>
        public bool IsNormal { get; init; }

        /// <summary>Anomaly score [0, 1] - higher means more anomalous.</summary>
        public double AnomalyScore { get; init; }

        /// <summary>Confidence in the detection [0, 1].</summary>
        public double Confidence { get; init; }

        /// <summary>
        /// Recommended action based on score and confidence.
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
        /// <summary>Total items checked.</summary>
        public int TotalItems { get; init; }

        /// <summary>Number of anomalies detected.</summary>
        public int AnomaliesDetected { get; init; }

        /// <summary>Percentage of anomalies.</summary>
        public double AnomalyRate { get; init; }

        /// <summary>Individual results for each sample.</summary>
        public AnomalyResult[] Results { get; init; }

        /// <summary>Indices and scores of top anomalies.</summary>
        public (int Index, double Score)[] TopAnomalies { get; init; }
    }

    /// <summary>
    /// Feature contribution to anomaly score.
    /// </summary>
    public class FeatureContribution
    {
        /// <summary>Feature name or index.</summary>
        public string FeatureName { get; init; }

        /// <summary>Contribution score (higher = more unusual).</summary>
        public double Contribution { get; init; }
    }

    /// <summary>
    /// Detector metadata (no F# types exposed).
    /// </summary>
    public class DetectorMetadata
    {
        public Sensitivity Sensitivity { get; init; }
        public TimeSpan TrainingTime { get; init; }
        public int NumFeatures { get; init; }
        public int NumNormalSamples { get; init; }
        public DateTime CreatedAt { get; init; }
        public string Note { get; init; }
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

            if (FSharpResult<AnomalyDetector.AnomalyResult, string>.get_IsError(result))
            {
                var error = ((FSharpResult<AnomalyDetector.AnomalyResult, string>.Error)result).ErrorValue;
                throw new InvalidOperationException($"Detection failed: {error}");
            }

            var anomalyResult = ((FSharpResult<AnomalyDetector.AnomalyResult, string>.Ok)result).ResultValue;

            return new AnomalyResult
            {
                IsAnomaly = anomalyResult.IsAnomaly,
                IsNormal = anomalyResult.IsNormal,
                AnomalyScore = anomalyResult.AnomalyScore,
                Confidence = anomalyResult.Confidence
            };
        }

        public BatchResults CheckBatch(double[][] samples)
        {
            var result = AnomalyDetector.checkBatch(samples, _detector);

            if (FSharpResult<AnomalyDetector.BatchResults, string>.get_IsError(result))
            {
                var error = ((FSharpResult<AnomalyDetector.BatchResults, string>.Error)result).ErrorValue;
                throw new InvalidOperationException($"Batch detection failed: {error}");
            }

            var batchResults = ((FSharpResult<AnomalyDetector.BatchResults, string>.Ok)result).ResultValue;

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
                    Confidence = r.Confidence
                }).ToArray(),
                TopAnomalies = batchResults.TopAnomalies.Select(t => (t.Item1, t.Item2)).ToArray()
            };
        }

        public FeatureContribution[] Explain(double[] sample, double[][] trainingData)
        {
            var result = AnomalyDetector.explain(sample, _detector, trainingData);

            if (FSharpResult<Tuple<string, double>[], string>.get_IsError(result))
            {
                var error = ((FSharpResult<Tuple<string, double>[], string>.Error)result).ErrorValue;
                throw new InvalidOperationException($"Explanation failed: {error}");
            }

            var contributions = ((FSharpResult<Tuple<string, double>[], string>.Ok)result).ResultValue;

            return contributions.Select(c => new FeatureContribution
            {
                FeatureName = c.Item1,
                Contribution = c.Item2
            }).ToArray();
        }

        public void SaveTo(string path)
        {
            var result = AnomalyDetector.save(path, _detector);

            if (FSharpResult<Microsoft.FSharp.Core.Unit, string>.get_IsError(result))
            {
                var error = ((FSharpResult<Microsoft.FSharp.Core.Unit, string>.Error)result).ErrorValue;
                throw new InvalidOperationException($"Failed to save detector: {error}");
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
                    Note = FSharpOption<string>.get_IsSome(metadata.Note) ? metadata.Note.Value : null
                };
            }
        }
    }
}
