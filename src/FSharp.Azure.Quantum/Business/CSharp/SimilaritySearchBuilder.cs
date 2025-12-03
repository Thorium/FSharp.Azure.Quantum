using System;
using System.Linq;
using FSharp.Azure.Quantum.Core.BackendAbstraction;
using Microsoft.FSharp.Core;

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
    public class SimilaritySearchBuilder<T>
    {
        private Tuple<T, double[]>[] _items;
        private SimilarityMetric _metric = SimilarityMetric.Cosine;
        private double _threshold = 0.7;
        private IQuantumBackend _backend = null;
        private int _shots = 1000;
        private bool _verbose = false;
        private string _savePath = null;
        private string _note = null;

        /// <summary>
        /// Index items with their feature vectors.
        /// </summary>
        public SimilaritySearchBuilder<T> IndexItems(T[] items, double[][] features)
        {
            if (items.Length != features.Length)
                throw new ArgumentException("Items and features must have same length");

            _items = items.Zip(features, (item, feat) => Tuple.Create(item, feat)).ToArray();
            return this;
        }

        /// <summary>
        /// Index items with feature extraction function.
        /// </summary>
        public SimilaritySearchBuilder<T> IndexItems(T[] items, Func<T, double[]> featureExtractor)
        {
            _items = items.Select(item => Tuple.Create(item, featureExtractor(item))).ToArray();
            return this;
        }

        /// <summary>
        /// Index items with tuples of (item, features).
        /// </summary>
        public SimilaritySearchBuilder<T> IndexItems(Tuple<T, double[]>[] items)
        {
            _items = items;
            return this;
        }

        /// <summary>
        /// Set similarity metric.
        /// Cosine: Good for text, high-dimensional data (default)
        /// Euclidean: Good for spatial data, images
        /// QuantumKernel: Maximum accuracy, slower
        /// </summary>
        public SimilaritySearchBuilder<T> WithMetric(SimilarityMetric metric)
        {
            _metric = metric;
            return this;
        }

        /// <summary>
        /// Set minimum similarity threshold [0, 1].
        /// Default: 0.7
        /// </summary>
        public SimilaritySearchBuilder<T> WithThreshold(double threshold)
        {
            _threshold = threshold;
            return this;
        }

        /// <summary>
        /// Specify quantum backend (for QuantumKernel metric).
        /// Default: LocalBackend (simulation)
        /// </summary>
        public SimilaritySearchBuilder<T> WithBackend(IQuantumBackend backend)
        {
            _backend = backend;
            return this;
        }

        /// <summary>
        /// Set number of measurement shots for quantum kernel.
        /// Default: 1000
        /// </summary>
        public SimilaritySearchBuilder<T> WithShots(int shots)
        {
            _shots = shots;
            return this;
        }

        /// <summary>
        /// Enable verbose logging during indexing.
        /// Default: false
        /// </summary>
        public SimilaritySearchBuilder<T> WithVerbose(bool verbose = true)
        {
            _verbose = verbose;
            return this;
        }

        /// <summary>
        /// Save index to specified path.
        /// </summary>
        public SimilaritySearchBuilder<T> SaveIndexTo(string path)
        {
            _savePath = path;
            return this;
        }

        /// <summary>
        /// Add optional note about the index (saved in metadata).
        /// </summary>
        public SimilaritySearchBuilder<T> WithNote(string note)
        {
            _note = note;
            return this;
        }

        /// <summary>
        /// Build the similarity search index.
        /// Returns an index ready for similarity searches.
        /// </summary>
        /// <exception cref="InvalidOperationException">Thrown if indexing fails.</exception>
        public ISimilaritySearchIndex<T> Build()
        {
            // Build F# problem specification
            var problem = new SimilaritySearch.SearchProblem<T>(
                Items: _items,
                Metric: ConvertMetric(_metric),
                Threshold: _threshold,
                Backend: _backend != null ? FSharpOption<IQuantumBackend>.Some(_backend) : FSharpOption<IQuantumBackend>.None,
                Shots: _shots,
                Verbose: _verbose,
                SavePath: _savePath != null ? FSharpOption<string>.Some(_savePath) : FSharpOption<string>.None,
                Note: _note != null ? FSharpOption<string>.Some(_note) : FSharpOption<string>.None
            );

            // Build index
            var result = SimilaritySearch.build(problem);

            if (FSharpResult<SimilaritySearch.SearchIndex<T>, string>.get_IsError(result))
            {
                var error = ((FSharpResult<SimilaritySearch.SearchIndex<T>, string>.Error)result).ErrorValue;
                throw new InvalidOperationException($"Indexing failed: {error}");
            }

            var index = ((FSharpResult<SimilaritySearch.SearchIndex<T>, string>.Ok)result).ResultValue;
            return new SimilaritySearchIndexWrapper<T>(index);
        }

        /// <summary>
        /// Load a previously saved index from file.
        /// </summary>
        public static ISimilaritySearchIndex<T> LoadFrom(string path)
        {
            var result = SimilaritySearch.load<T>(path);

            if (FSharpResult<SimilaritySearch.SearchIndex<T>, string>.get_IsError(result))
            {
                var error = ((FSharpResult<SimilaritySearch.SearchIndex<T>, string>.Error)result).ErrorValue;
                throw new InvalidOperationException($"Failed to load index: {error}");
            }

            var index = ((FSharpResult<SimilaritySearch.SearchIndex<T>, string>.Ok)result).ResultValue;
            return new SimilaritySearchIndexWrapper<T>(index);
        }

        private static SimilaritySearch.SimilarityMetric ConvertMetric(SimilarityMetric metric)
        {
            return metric switch
            {
                SimilarityMetric.Cosine => SimilaritySearch.SimilarityMetric.Cosine,
                SimilarityMetric.Euclidean => SimilaritySearch.SimilarityMetric.Euclidean,
                SimilarityMetric.QuantumKernel => SimilaritySearch.SimilarityMetric.QuantumKernel,
                _ => SimilaritySearch.SimilarityMetric.Cosine
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
        QuantumKernel
    }

    /// <summary>
    /// Interface for similarity search index.
    /// </summary>
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
        void SaveTo(string path);

        /// <summary>
        /// Get index metadata.
        /// </summary>
        IndexMetadata Metadata { get; }
    }

    /// <summary>
    /// Search results (no F# types exposed).
    /// </summary>
    public class SearchResults<T>
    {
        /// <summary>Query item.</summary>
        public T Query { get; init; }

        /// <summary>Top matches sorted by similarity.</summary>
        public Match<T>[] Matches { get; init; }

        /// <summary>Time taken to search.</summary>
        public TimeSpan SearchTime { get; init; }
    }

    /// <summary>
    /// A single similarity match.
    /// </summary>
    public class Match<T>
    {
        /// <summary>Matched item.</summary>
        public T Item { get; init; }

        /// <summary>Similarity score [0, 1] - higher is more similar.</summary>
        public double Similarity { get; init; }

        /// <summary>Rank in results (1 = most similar).</summary>
        public int Rank { get; init; }
    }

    /// <summary>
    /// Group of duplicate/similar items.
    /// </summary>
    public class DuplicateGroup<T>
    {
        /// <summary>Representative item for the group.</summary>
        public T Representative { get; init; }

        /// <summary>All items in the group (including representative).</summary>
        public T[] Items { get; init; }

        /// <summary>Average similarity within the group.</summary>
        public double AvgSimilarity { get; init; }
    }

    /// <summary>
    /// Index metadata (no F# types exposed).
    /// </summary>
    public class IndexMetadata
    {
        public int NumItems { get; init; }
        public int NumFeatures { get; init; }
        public SimilarityMetric Metric { get; init; }
        public DateTime CreatedAt { get; init; }
        public string Note { get; init; }
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

            if (FSharpResult<SimilaritySearch.SearchResults<T>, string>.get_IsError(result))
            {
                var error = ((FSharpResult<SimilaritySearch.SearchResults<T>, string>.Error)result).ErrorValue;
                throw new InvalidOperationException($"Search failed: {error}");
            }

            var searchResults = ((FSharpResult<SimilaritySearch.SearchResults<T>, string>.Ok)result).ResultValue;

            return new SearchResults<T>
            {
                Query = searchResults.Query,
                Matches = searchResults.Matches.Select(m => new Match<T>
                {
                    Item = m.Item,
                    Similarity = m.Similarity,
                    Rank = m.Rank
                }).ToArray(),
                SearchTime = searchResults.SearchTime
            };
        }

        public Match<T>[] FindAllSimilar(double[] queryFeatures)
        {
            var result = SimilaritySearch.findAllSimilar(queryFeatures, _index);

            if (FSharpResult<SimilaritySearch.Match<T>[], string>.get_IsError(result))
            {
                var error = ((FSharpResult<SimilaritySearch.Match<T>[], string>.Error)result).ErrorValue;
                throw new InvalidOperationException($"Search failed: {error}");
            }

            var matches = ((FSharpResult<SimilaritySearch.Match<T>[], string>.Ok)result).ResultValue;

            return matches.Select(m => new Match<T>
            {
                Item = m.Item,
                Similarity = m.Similarity,
                Rank = m.Rank
            }).ToArray();
        }

        public DuplicateGroup<T>[] FindDuplicates(double threshold)
        {
            var result = SimilaritySearch.findDuplicates(threshold, _index);

            if (FSharpResult<SimilaritySearch.DuplicateGroup<T>[], string>.get_IsError(result))
            {
                var error = ((FSharpResult<SimilaritySearch.DuplicateGroup<T>[], string>.Error)result).ErrorValue;
                throw new InvalidOperationException($"Duplicate detection failed: {error}");
            }

            var groups = ((FSharpResult<SimilaritySearch.DuplicateGroup<T>[], string>.Ok)result).ResultValue;

            return groups.Select(g => new DuplicateGroup<T>
            {
                Representative = g.Representative,
                Items = g.Items,
                AvgSimilarity = g.AvgSimilarity
            }).ToArray();
        }

        public T[][] Cluster(int numClusters, int maxIterations = 100)
        {
            var result = SimilaritySearch.cluster(numClusters, maxIterations, _index);

            if (FSharpResult<T[][], string>.get_IsError(result))
            {
                var error = ((FSharpResult<T[][], string>.Error)result).ErrorValue;
                throw new InvalidOperationException($"Clustering failed: {error}");
            }

            return ((FSharpResult<T[][], string>.Ok)result).ResultValue;
        }

        public void SaveTo(string path)
        {
            var result = SimilaritySearch.save(path, _index);

            if (FSharpResult<Microsoft.FSharp.Core.Unit, string>.get_IsError(result))
            {
                var error = ((FSharpResult<Microsoft.FSharp.Core.Unit, string>.Error)result).ErrorValue;
                throw new InvalidOperationException($"Failed to save index: {error}");
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
                    Note = FSharpOption<string>.get_IsSome(metadata.Note) ? metadata.Note.Value : null
                };
            }
        }
    }
}
