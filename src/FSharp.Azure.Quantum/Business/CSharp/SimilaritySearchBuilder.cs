using System;
using System.Linq;
using FSharp.Azure.Quantum.Core;
using Microsoft.FSharp.Core;
using static FSharp.Azure.Quantum.Core.BackendAbstraction;

namespace FSharp.Azure.Quantum.Business.CSharp
{
    /// <summary>
    /// C# Fluent API for Similarity Search
    ///
    /// Find similar items using quantum kernel similarity.
    /// Use this for recommendations, duplicate detection, and content similarity.
    ///
    /// Example:
    /// <code>
    /// var matcher = new SimilaritySearchBuilder&lt;Product&gt;()
    ///     .IndexItems(products, p => p.Features)
    ///     .Build();
    ///
    /// var similar = matcher.FindSimilar(currentProduct, top: 5);
    /// </code>
    /// </summary>
    /// <typeparam name="T">The type of items to index and search.</typeparam>
    public class SimilaritySearchBuilder<T>
    {
        private Tuple<T, double[]>[]? _items;
        private SimilarityMetric _metric = SimilarityMetric.Cosine;
        private double _threshold = 0.7;
        private IQuantumBackend? _backend;
        private int _shots = 1000;
        private bool _verbose;
        private string? _savePath;
        private string? _note;

        /// <summary>
        /// Index items with their feature vectors.
        /// </summary>
        /// <param name="items">Items to index.</param>
        /// <param name="features">Feature vectors for each item.</param>
        /// <returns>The builder instance for chaining.</returns>
        public SimilaritySearchBuilder<T> IndexItems(T[] items, double[][] features)
        {
            ArgumentNullException.ThrowIfNull(items);
            ArgumentNullException.ThrowIfNull(features);

            if (items.Length != features.Length)
            {
                throw new ArgumentException("Items and features must have same length");
            }

            _items = items.Zip(features, (item, feat) => Tuple.Create(item, feat)).ToArray();
            return this;
        }

        /// <summary>
        /// Index items with feature extraction function.
        /// </summary>
        /// <param name="items">Items to index.</param>
        /// <param name="featureExtractor">Function to extract features from each item.</param>
        /// <returns>The builder instance for chaining.</returns>
        public SimilaritySearchBuilder<T> IndexItems(T[] items, Func<T, double[]> featureExtractor)
        {
            ArgumentNullException.ThrowIfNull(items);
            ArgumentNullException.ThrowIfNull(featureExtractor);
            _items = items.Select(item => Tuple.Create(item, featureExtractor(item))).ToArray();
            return this;
        }

        /// <summary>
        /// Index items with tuples of (item, features).
        /// </summary>
        /// <param name="items">Tuples of (item, features) to index.</param>
        /// <returns>The builder instance for chaining.</returns>
        public SimilaritySearchBuilder<T> IndexItems(Tuple<T, double[]>[] items)
        {
            ArgumentNullException.ThrowIfNull(items);
            _items = items;
            return this;
        }

        /// <summary>
        /// Set similarity metric.
        /// Cosine: Good for text, high-dimensional data (default)
        /// Euclidean: Good for spatial data, images
        /// QuantumKernel: Maximum accuracy, slower.
        /// </summary>
        /// <param name="metric">Similarity metric to use.</param>
        /// <returns>The builder instance for chaining.</returns>
        public SimilaritySearchBuilder<T> WithMetric(SimilarityMetric metric)
        {
            _metric = metric;
            return this;
        }

        /// <summary>
        /// Set minimum similarity threshold [0, 1].
        /// Default: 0.7.
        /// </summary>
        /// <param name="threshold">Minimum similarity threshold.</param>
        /// <returns>The builder instance for chaining.</returns>
        public SimilaritySearchBuilder<T> WithThreshold(double threshold)
        {
            _threshold = threshold;
            return this;
        }

        /// <summary>
        /// Specify quantum backend (for QuantumKernel metric).
        /// Default: LocalBackend (simulation).
        /// </summary>
        /// <param name="backend">Backend to execute</param>
        /// <returns>The builder instance for chaining.</returns>
        public SimilaritySearchBuilder<T> WithBackend(IQuantumBackend backend)
        {
            ArgumentNullException.ThrowIfNull(backend);
            _backend = backend;
            return this;
        }

        /// <summary>
        /// Set number of measurement shots for quantum kernel.
        /// Default: 1000.
        /// </summary>
        /// <param name="shots">Number of measurement shots.</param>
        /// <returns>The builder instance for chaining.</returns>
        public SimilaritySearchBuilder<T> WithShots(int shots)
        {
            _shots = shots;
            return this;
        }

        /// <summary>
        /// Enable verbose logging during indexing.
        /// Default: false.
        /// </summary>
        /// <param name="verbose">Whether to enable verbose logging.</param>
        /// <returns>The builder instance for chaining.</returns>
        public SimilaritySearchBuilder<T> WithVerbose(bool verbose = true)
        {
            _verbose = verbose;
            return this;
        }

        /// <summary>
        /// Save index to specified path.
        /// </summary>
        /// <param name="path">Path to save the index.</param>
        /// <returns>The builder instance for chaining.</returns>
        public SimilaritySearchBuilder<T> SaveIndexTo(string path)
        {
            ArgumentNullException.ThrowIfNull(path);
            _savePath = path;
            return this;
        }

        /// <summary>
        /// Add optional note about the index (saved in metadata).
        /// </summary>
        /// <param name="note">Note to save with the index.</param>
        /// <returns>The builder instance for chaining.</returns>
        public SimilaritySearchBuilder<T> WithNote(string note)
        {
            ArgumentNullException.ThrowIfNull(note);
            _note = note;
            return this;
        }

        /// <summary>
        /// Build the similarity search index.
        /// Returns an index ready for similarity searches.
        /// </summary>
        /// <exception cref="InvalidOperationException">Thrown if indexing fails.</exception>
        /// <returns>The built similarity search index.</returns>
        public ISimilaritySearchIndex<T> Build()
        {
            // Build F# problem specification
            var problem = new SimilaritySearch.SearchProblem<T>(
                items: _items,
                metric: ConvertMetric(_metric),
                threshold: _threshold,
                backend: _backend != null ? FSharpOption<IQuantumBackend>.Some(_backend) : FSharpOption<IQuantumBackend>.None,
                shots: _shots,
                verbose: _verbose,
                savePath: _savePath != null ? FSharpOption<string>.Some(_savePath) : FSharpOption<string>.None,
                note: _note != null ? FSharpOption<string>.Some(_note) : FSharpOption<string>.None,
                progressReporter: FSharpOption<Progress.IProgressReporter>.None,
                cancellationToken: FSharpOption<System.Threading.CancellationToken>.None,
                logger: FSharpOption<Microsoft.Extensions.Logging.ILogger>.None);

            // Build index
            var result = SimilaritySearch.build(problem);

            if (result.IsError)
            {
                throw new InvalidOperationException($"Indexing failed: {result.ErrorValue.Message}");
            }

            var index = result.ResultValue;
            return new SimilaritySearchIndexWrapper<T>(index);
        }

        /// <summary>
        /// Load a previously saved index from file.
        /// </summary>
        /// <param name="path">Path to the saved index file.</param>
        /// <returns>The loaded similarity search index.</returns>
        public static ISimilaritySearchIndex<T> LoadFrom(string path)
        {
            ArgumentNullException.ThrowIfNull(path);
            var result = SimilaritySearch.load<T>(path);

            if (result.IsError)
            {
                throw new InvalidOperationException($"Failed to load index: {result.ErrorValue.Message}");
            }

            var index = result.ResultValue;
            return new SimilaritySearchIndexWrapper<T>(index);
        }

        private static SimilaritySearch.SimilarityMetric ConvertMetric(SimilarityMetric metric)
        {
            return metric switch
            {
                SimilarityMetric.Cosine => SimilaritySearch.SimilarityMetric.Cosine,
                SimilarityMetric.Euclidean => SimilaritySearch.SimilarityMetric.Euclidean,
                SimilarityMetric.QuantumKernel => SimilaritySearch.SimilarityMetric.QuantumKernel,
                _ => SimilaritySearch.SimilarityMetric.Cosine,
            };
        }
    }

    /// <summary>
    /// Similarity metric for comparing items.
    /// </summary>
    public enum SimilarityMetric
    {
        /// <summary>Cosine similarity - good for text and high-dimensional data.</summary>
        Cosine,

        /// <summary>Euclidean distance - good for spatial data and images.</summary>
        Euclidean,

        /// <summary>Quantum kernel similarity - maximum accuracy, computationally expensive.</summary>
        QuantumKernel,
    }

    /// <summary>
    /// Index metadata (no F# types exposed).
    /// </summary>
    public class IndexMetadata
    {
        /// <summary>Gets number of indexed items.</summary>
        public int NumItems { get; init; }

        /// <summary>Gets number of features per item.</summary>
        public int NumFeatures { get; init; }

        /// <summary>Gets similarity metric used by the index.</summary>
        public SimilarityMetric Metric { get; init; }

        /// <summary>Gets timestamp when the index was created.</summary>
        public DateTime CreatedAt { get; init; }

        /// <summary>Gets optional user note stored with the index.</summary>
        public string? Note { get; init; }
    }

    /// <summary>
    /// Interface for similarity search index.
    /// </summary>
    /// <typeparam name="T">The type of items in the index.</typeparam>
    public interface ISimilaritySearchIndex<T>
    {
        /// <summary>
        /// Find top N most similar items to the query item.
        /// </summary>
        /// <param name="queryItem">Item to find similar items for.</param>
        /// <param name="queryFeatures">Feature vector for query item.</param>
        /// <param name="topN">Number of results to return.</param>
        /// <returns>Search results with top matches.</returns>
        SearchResults<T> FindSimilar(T queryItem, double[] queryFeatures, int topN);

        /// <summary>
        /// Find all items similar to query above threshold.
        /// </summary>
        /// <param name="queryFeatures">Feature vector for query.</param>
        /// <returns>All matching items.</returns>
        Match<T>[] FindAllSimilar(double[] queryFeatures);

        /// <summary>
        /// Find groups of duplicate/near-duplicate items.
        /// </summary>
        /// <param name="threshold">Similarity threshold for duplicates.</param>
        /// <returns>Groups of similar items.</returns>
        DuplicateGroup<T>[] FindDuplicates(double threshold);

        /// <summary>
        /// Cluster items into groups based on similarity.
        /// </summary>
        /// <param name="numClusters">Number of clusters to create.</param>
        /// <param name="maxIterations">Maximum iterations for clustering.</param>
        /// <returns>Array of clusters (each cluster is an array of items).</returns>
        T[][] Cluster(int numClusters, int maxIterations = 100);

        /// <summary>
        /// Save index to file.
        /// </summary>
        /// <param name="path">Path to save the index.</param>
        void SaveTo(string path);

        /// <summary>
        /// Gets get index metadata.
        /// </summary>
        IndexMetadata Metadata { get; }
    }

    /// <summary>
    /// Search results (no F# types exposed).
    /// </summary>
    /// <typeparam name="T">The type of items in the search results.</typeparam>
    public class SearchResults<T>
    {
        /// <summary>Gets query item.</summary>
        public required T Query { get; init; }

        /// <summary>Gets matching items and their similarity scores.</summary>
        public required Match<T>[] Matches { get; init; }

        /// <summary>Gets time taken to perform the search.</summary>
        public TimeSpan SearchTime { get; init; }
    }

    /// <summary>
    /// Single match result (no F# types exposed).
    /// </summary>
    /// <typeparam name="T">The type of the matched item.</typeparam>
    public class Match<T>
    {
        /// <summary>Gets matched item.</summary>
        public required T Item { get; init; }

        /// <summary>Gets similarity score (higher means more similar).</summary>
        public double Similarity { get; init; }

        /// <summary>Gets rank of the match in results (0-based).</summary>
        public int Rank { get; init; }
    }

    /// <summary>
    /// Group of duplicate/near-duplicate items (no F# types exposed).
    /// </summary>
    /// <typeparam name="T">The type of items in the duplicate group.</typeparam>
    public class DuplicateGroup<T>
    {
        /// <summary>Gets representative item for the group.</summary>
        public required T Representative { get; init; }

        /// <summary>Gets all items in the group.</summary>
        public required T[] Items { get; init; }

        /// <summary>Gets average similarity within the group.</summary>
        public double AvgSimilarity { get; init; }
    }

    // ============================================================================
    // INTERNAL WRAPPER - Hides F# types from C# consumers
    // ============================================================================
    internal class SimilaritySearchIndexWrapper<T> : ISimilaritySearchIndex<T>
    {
        private readonly SimilaritySearch.SearchIndex<T> _index;

        public SimilaritySearchIndexWrapper(SimilaritySearch.SearchIndex<T> index)
        {
            _index = index;
        }

        public SearchResults<T> FindSimilar(T queryItem, double[] queryFeatures, int topN)
        {
            var result = SimilaritySearch.findSimilar(queryItem, queryFeatures, topN, _index);

            if (result.IsError)
            {
                throw new InvalidOperationException($"Search failed: {result.ErrorValue.Message}");
            }

            var searchResults = result.ResultValue;

            return new SearchResults<T>
            {
                Query = searchResults.Query,
                Matches = searchResults.Matches.Select(m => new Match<T>
                {
                    Item = m.Item,
                    Similarity = m.Similarity,
                    Rank = m.Rank,
                }).ToArray(),
                SearchTime = searchResults.SearchTime,
            };
        }

        public Match<T>[] FindAllSimilar(double[] queryFeatures)
        {
            var result = SimilaritySearch.findAllSimilar(queryFeatures, _index);

            if (result.IsError)
            {
                throw new InvalidOperationException($"Search failed: {result.ErrorValue.Message}");
            }

            var matches = result.ResultValue;

            return matches.Select(m => new Match<T>
            {
                Item = m.Item,
                Similarity = m.Similarity,
                Rank = m.Rank,
            }).ToArray();
        }

        public DuplicateGroup<T>[] FindDuplicates(double threshold)
        {
            var result = SimilaritySearch.findDuplicates(threshold, _index);

            if (result.IsError)
            {
                throw new InvalidOperationException($"Duplicate detection failed: {result.ErrorValue.Message}");
            }

            var groups = result.ResultValue;

            return groups.Select(g => new DuplicateGroup<T>
            {
                Representative = g.Representative,
                Items = g.Items,
                AvgSimilarity = g.AvgSimilarity,
            }).ToArray();
        }

        public T[][] Cluster(int numClusters, int maxIterations = 100)
        {
            var result = SimilaritySearch.cluster(numClusters, maxIterations, _index);

            if (result.IsError)
            {
                throw new InvalidOperationException($"Clustering failed: {result.ErrorValue.Message}");
            }

            return result.ResultValue;
        }

        public void SaveTo(string path)
        {
            var result = SimilaritySearch.save(path, _index);

            if (result.IsError)
            {
                throw new InvalidOperationException($"Failed to save index: {result.ErrorValue.Message}");
            }
        }

        public IndexMetadata Metadata
        {
            get
            {
                var metadata = _index.Metadata;
                var metric = metadata.Metric.IsCosine ? SimilarityMetric.Cosine
                           : metadata.Metric.IsEuclidean ? SimilarityMetric.Euclidean
                           : SimilarityMetric.QuantumKernel;

                return new IndexMetadata
                {
                    NumItems = metadata.NumItems,
                    NumFeatures = metadata.NumFeatures,
                    Metric = metric,
                    CreatedAt = metadata.CreatedAt,
                    Note = FSharpOption<string>.get_IsSome(metadata.Note) ? metadata.Note.Value : null,
                };
            }
        }
    }
}
