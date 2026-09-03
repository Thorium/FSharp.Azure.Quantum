namespace FSharp.Azure.Quantum.Business

open FSharp.Azure.Quantum.Backends
open FSharp.Azure.Quantum.Core
open System
open System.IO
open System.Text.Json
open FSharp.Azure.Quantum.Backends
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Backends
open FSharp.Azure.Quantum.MachineLearning
open FSharp.Azure.Quantum.Backends
open FSharp.Azure.Quantum
open Microsoft.Extensions.Logging

/// High-Level Similarity Search Builder - Business-First API
/// 
/// DESIGN PHILOSOPHY:
/// This is a BUSINESS DOMAIN API for finding similar items using quantum kernels
/// without understanding kernel methods, feature maps, or quantum computing.
/// 
/// WHAT IS SIMILARITY SEARCH:
/// Find items that are similar to a given item based on their features.
/// Uses quantum kernel methods to compute similarity in a high-dimensional space.
/// 
/// USE CASES:
/// - Product recommendations: "Customers who bought X also bought Y"
/// - Duplicate detection: Find near-duplicate records
/// - Content similarity: Similar documents, images, or articles
/// - Customer segmentation: Group similar customers
/// - Search ranking: Find most relevant results
/// - Pattern matching: Find similar patterns in data
/// 
/// EXAMPLE USAGE:
///   // Simple: Index items and find similar
///   let matcher = similaritySearch {
///       indexItems products
///   }
///   
///   // Find similar products
///   let similar = matcher |> SimilaritySearch.findSimilar currentProduct 5
///   
///   // Advanced: Full configuration
///   let matcher = similaritySearch {
///       indexItems products
///       
///       // Similarity configuration
///       similarityMetric Cosine  // or Euclidean, Kernel
///       threshold 0.7
///       
///       // Infrastructure
///       backend azureBackend
///       
///       // Persistence
///       saveIndexTo "product_index.dat"
///   }
module SimilaritySearch =
    
    // ========================================================================
    // CORE TYPES - Similarity Search Domain Model
    // ========================================================================
    
    /// Similarity metric to use
    type SimilarityMetric =
        /// Cosine similarity (default)
        | Cosine
        /// Euclidean distance
        | Euclidean
        /// Quantum kernel similarity
        | QuantumKernel
    
    /// Similarity search problem specification
    type SearchProblem<'T> = {
        /// Items to index (with feature vectors)
        Items: ('T * float array) array
        
        /// Similarity metric
        Metric: SimilarityMetric
        
        /// Minimum similarity threshold [0, 1]
        Threshold: float
        
        /// Quantum backend (for kernel similarity)
        Backend: IQuantumBackend option
        
        /// Number of measurement shots (for quantum kernel)
        Shots: int
        
        /// Verbose logging
        Verbose: bool
        
        /// Path to save index
        SavePath: string option
        
        /// Optional note
        Note: string option
        
        /// Optional progress reporter for real-time updates
        ProgressReporter: Core.Progress.IProgressReporter option
        
        /// Optional cancellation token for early termination
        CancellationToken: System.Threading.CancellationToken option
        
        /// Optional logger for structured logging
        Logger: ILogger option
    }
    
    /// Quantum kernel configuration retained in the index so genuine quantum
    /// kernels can be evaluated against novel query items at search time — not
    /// just looked up from the precomputed item-vs-item matrix.
    type QuantumKernelConfig = {
        Backend: IQuantumBackend
        FeatureMap: FeatureMapType
        Shots: int
    }

    /// Trained similarity search index
    type SearchIndex<'T> = {
        /// Indexed items with features
        Items: ('T * float array) array

        /// Similarity metric
        Metric: SimilarityMetric

        /// Threshold
        Threshold: float

        /// Quantum kernel matrix (precomputed for efficiency)
        KernelMatrix: float[,] option

        /// Retained quantum-kernel backend/config for in-memory builds. None for
        /// indices loaded from disk (a backend cannot be serialised) — such indices
        /// cannot run novel-query quantum-kernel searches and report an honest error
        /// rather than silently substituting a classical metric.
        QuantumConfig: QuantumKernelConfig option

        /// Metadata
        Metadata: IndexMetadata
    }

    and IndexMetadata = {
        NumItems: int
        NumFeatures: int
        Metric: SimilarityMetric
        CreatedAt: DateTime
        Note: string option
    }
    
    /// Similarity match result
    type Match<'T> = {
        /// Matched item
        Item: 'T
        
        /// Similarity score [0, 1] - higher is more similar
        Similarity: float
        
        /// Rank (1 = most similar)
        Rank: int
    }
    
    /// Search results
    type SearchResults<'T> = {
        /// Query item
        Query: 'T
        
        /// Top matches
        Matches: Match<'T> array
        
        /// Search time
        SearchTime: TimeSpan
    }
    
    /// Duplicate detection results
    type DuplicateGroup<'T> = {
        /// Representative item
        Representative: 'T
        
        /// Duplicates (including representative)
        Items: 'T array
        
        /// Average similarity within group
        AvgSimilarity: float
    }
    
    // ========================================================================
    // VALIDATION
    // ========================================================================
    
    /// Validate search problem
    let private validate (problem: SearchProblem<'T>) : QuantumResult<unit> =
        if problem.Items.Length = 0 then
            Error (QuantumError.ValidationError ("Input", "Items cannot be empty"))
        elif problem.Threshold < 0.0 || problem.Threshold > 1.0 then
            Error (QuantumError.ValidationError ("Input", "Threshold must be between 0.0 and 1.0"))
        elif problem.Shots < 1 then
            Error (QuantumError.ValidationError ("Input", "Shots must be at least 1"))
        else
            let numFeatures = snd problem.Items.[0] |> Array.length
            let allSameLength = 
                problem.Items 
                |> Array.forall (fun (_, features) -> features.Length = numFeatures)
            
            if not allSameLength then
                Error (QuantumError.ValidationError ("Input", "All feature vectors must have the same length"))
            else
                Ok ()
    
    // ========================================================================
    // SIMILARITY COMPUTATION
    // ========================================================================
    
    /// Compute cosine similarity between two vectors
    let private cosineSimilarity (a: float array) (b: float array) : float =
        let dotProduct = Array.zip a b |> Array.sumBy (fun (x, y) -> x * y)
        let normA = sqrt (a |> Array.sumBy (fun x -> x * x))
        let normB = sqrt (b |> Array.sumBy (fun x -> x * x))
        
        if normA = 0.0 || normB = 0.0 then
            0.0
        else
            dotProduct / (normA * normB)
    
    /// Compute Euclidean similarity (normalized to [0, 1])
    let private euclideanSimilarity (a: float array) (b: float array) : float =
        let distance = 
            Array.zip a b 
            |> Array.sumBy (fun (x, y) -> (x - y) ** 2.0)
            |> sqrt
        
        // Normalize to [0, 1] using sigmoid-like function
        1.0 / (1.0 + distance)
    
    /// Compute quantum kernel similarity
    let private quantumKernelSimilarity 
        (backend: IQuantumBackend)
        (featureMap: FeatureMapType)
        (numQubits: int)
        (shots: int)
        (a: float array)
        (b: float array)
        : QuantumResult<float> =
        
        QuantumKernels.computeKernel backend featureMap a b shots
    
    // ========================================================================
    // INDEX BUILDING
    // ========================================================================
    
    /// Build similarity search index
    let build (problem: SearchProblem<'T>) : QuantumResult<SearchIndex<'T>> =
        validate problem
        |> Result.bind (fun () ->
            
            let startTime = DateTime.UtcNow
            let numFeatures = snd problem.Items.[0] |> Array.length
            
            if problem.Verbose then
                logInfo problem.Logger "Building similarity index..."
                logInfo problem.Logger ($"  Items: %d{problem.Items.Length}")
                logInfo problem.Logger ($"  Features: %d{numFeatures}")
                logInfo problem.Logger ($"  Metric: %A{problem.Metric}")
            
            // Precompute kernel matrix if using quantum kernel. A QuantumKernel index
            // must genuinely use the quantum kernel: if the kernel computation fails we
            // propagate the technical error rather than silently substituting cosine
            // similarity (which would mislabel a classical result as a quantum search).
            let kernelResult : QuantumResult<float[,] option * QuantumKernelConfig option> =
                match problem.Metric with
                | QuantumKernel ->
                    let backend =
                        match problem.Backend with
                        | Some b -> b
                        | None -> LocalBackend.LocalBackend() :> IQuantumBackend

                    let featureMap = FeatureMapType.ZZFeatureMap 2
                    let features = problem.Items |> Array.map snd

                    if problem.Verbose then
                        logInfo problem.Logger "  Computing quantum kernel matrix..."

                    QuantumKernels.computeKernelMatrix backend featureMap features problem.Shots
                    |> Result.map (fun matrix ->
                        Some matrix, Some { Backend = backend; FeatureMap = featureMap; Shots = problem.Shots })

                | Cosine | Euclidean -> Ok (None, None)

            kernelResult
            |> Result.map (fun (kernelMatrix, quantumConfig) ->
                let endTime = DateTime.UtcNow

                if problem.Verbose then
                    logInfo problem.Logger (sprintf "[OK] Index built in %A" (endTime - startTime))

                {
                    Items = problem.Items
                    Metric = problem.Metric
                    Threshold = problem.Threshold
                    KernelMatrix = kernelMatrix
                    QuantumConfig = quantumConfig
                    Metadata = {
                        NumItems = problem.Items.Length
                        NumFeatures = numFeatures
                        Metric = problem.Metric
                        CreatedAt = startTime
                        Note = problem.Note
                    }
                }))
    
    // ========================================================================
    // SIMILARITY SEARCH
    // ========================================================================

    /// Compute genuine quantum-kernel similarities between a query and every indexed
    /// item using the retained backend. One circuit evaluation per item — the real
    /// cost of a quantum kernel. Returns an error (never silently falls back to a
    /// classical metric) when no live backend is available, e.g. an index loaded from
    /// disk: the caller asked for a quantum-kernel search and must get one or an error.
    let private quantumKernelSimilarities
        (index: SearchIndex<'T>)
        (queryFeatures: float array)
        : QuantumResult<('T * float) array> =
        match index.QuantumConfig with
        | None ->
            Error (QuantumError.Other
                "QuantumKernel search requires a live backend. This index was loaded from disk (or built without quantum config); rebuild it with a backend to run quantum-kernel queries.")
        | Some cfg ->
            (Ok [], index.Items)
            ||> Array.fold (fun acc (item, features) ->
                acc |> Result.bind (fun sims ->
                    QuantumKernels.computeKernel cfg.Backend cfg.FeatureMap queryFeatures features cfg.Shots
                    |> Result.map (fun sim -> (item, sim) :: sims)))
            |> Result.map (List.rev >> Array.ofList)

    /// Find top N most similar items
    let findSimilar
        (queryItem: 'T)
        (queryFeatures: float array)
        (topN: int)
        (index: SearchIndex<'T>)
        : QuantumResult<SearchResults<'T>> =

        let startTime = DateTime.UtcNow

        // Find query index if item is in index
        let queryIdx =
            index.Items
            |> Array.tryFindIndex (fun (item, _) -> obj.Equals(item, queryItem))

        // Compute similarities
        let similaritiesResult : QuantumResult<('T * float) array> =
            match index.Metric with
            | QuantumKernel when index.KernelMatrix.IsSome && queryIdx.IsSome ->
                let idx = queryIdx.Value
                // Query is an indexed item: reuse the precomputed kernel row.
                Ok [| for i in 0 .. index.Items.Length - 1 ->
                        if i = idx then
                            (fst index.Items.[i], 1.0)  // Self-similarity is 1.0
                        else
                            (fst index.Items.[i], index.KernelMatrix.Value.[idx, i]) |]

            | QuantumKernel ->
                // Novel query: evaluate the genuine quantum kernel against each item.
                quantumKernelSimilarities index queryFeatures

            | Cosine ->
                Ok (index.Items
                    |> Array.map (fun (item, features) ->
                        (item, cosineSimilarity queryFeatures features)))

            | Euclidean ->
                Ok (index.Items
                    |> Array.map (fun (item, features) ->
                        (item, euclideanSimilarity queryFeatures features)))

        similaritiesResult
        |> Result.map (fun similarities ->
            // Filter by threshold and exclude exact match
            let filtered =
                similarities
                |> Array.filter (fun (item, sim) ->
                    sim >= index.Threshold && not (obj.Equals(item, queryItem)))
                |> Array.sortByDescending snd
                |> Array.truncate topN

            let matches =
                filtered
                |> Array.mapi (fun i (item, sim) ->
                    {
                        Item = item
                        Similarity = sim
                        Rank = i + 1
                    })

            {
                Query = queryItem
                Matches = matches
                SearchTime = DateTime.UtcNow - startTime
            })

    /// Find all similar items above threshold
    let findAllSimilar
        (queryFeatures: float array)
        (index: SearchIndex<'T>)
        : QuantumResult<Match<'T> array> =

        // Compute similarities for all items
        let similaritiesResult : QuantumResult<('T * float) array> =
            match index.Metric with
            | Cosine ->
                Ok (index.Items
                    |> Array.map (fun (item, features) ->
                        (item, cosineSimilarity queryFeatures features)))

            | Euclidean ->
                Ok (index.Items
                    |> Array.map (fun (item, features) ->
                        (item, euclideanSimilarity queryFeatures features)))

            | QuantumKernel ->
                // Genuine quantum kernel against each item (no classical substitution).
                quantumKernelSimilarities index queryFeatures

        similaritiesResult
        |> Result.map (fun similarities ->
            similarities
            |> Array.filter (fun (_, sim) -> sim >= index.Threshold)
            |> Array.sortByDescending snd
            |> Array.mapi (fun i (item, sim) ->
                {
                    Item = item
                    Similarity = sim
                    Rank = i + 1
                }))
    
    // ========================================================================
    // DUPLICATE DETECTION
    // ========================================================================
    
    /// Find duplicate groups (items similar to each other)
    let findDuplicates
        (threshold: float)
        (index: SearchIndex<'T>)
        : QuantumResult<DuplicateGroup<'T> array> =
        
        if threshold < 0.0 || threshold > 1.0 then
            Error (QuantumError.ValidationError ("Threshold", "must be between 0.0 and 1.0"))
        else
            // Compute all pairwise similarities using functional approach
            let n = index.Items.Length

            // For a quantum-kernel index ensure a genuine kernel matrix is available:
            // reuse the precomputed one, else compute it via the retained backend. We
            // never substitute cosine for a quantum-kernel duplicate search.
            let matrixResult : QuantumResult<float[,] option> =
                match index.Metric with
                | QuantumKernel ->
                    match index.KernelMatrix with
                    | Some m -> Ok (Some m)
                    | None ->
                        match index.QuantumConfig with
                        | Some cfg ->
                            QuantumKernels.computeKernelMatrix cfg.Backend cfg.FeatureMap (index.Items |> Array.map snd) cfg.Shots
                            |> Result.map Some
                        | None ->
                            Error (QuantumError.Other
                                "QuantumKernel duplicate detection requires a precomputed kernel matrix or a live backend; this index (loaded from disk) has neither.")
                | Cosine | Euclidean -> Ok None

            matrixResult |> Result.bind (fun kernelOpt ->

            let computeSimilarity i j repFeatures features =
                match index.Metric with
                | Cosine -> cosineSimilarity repFeatures features
                | Euclidean -> euclideanSimilarity repFeatures features
                | QuantumKernel ->
                    match kernelOpt with
                    | Some matrix -> matrix.[i, j]
                    | None -> cosineSimilarity repFeatures features  // unreachable: guarded above

            let rec findGroups i visited groups =
                if i >= n then
                    groups
                elif Set.contains i visited then
                    findGroups (i + 1) visited groups
                else
                    let (representative, repFeatures) = index.Items.[i]
                    
                    // Find all items similar to representative
                    let (duplicateItems, newVisited) =
                        [i + 1 .. n - 1]
                        |> List.fold (fun (dups, vis) j ->
                            if Set.contains j vis then
                                (dups, vis)
                            else
                                let (item, features) = index.Items.[j]
                                let similarity = computeSimilarity i j repFeatures features
                                if similarity >= threshold then
                                    ((item, similarity) :: dups, Set.add j vis)
                                else
                                    (dups, vis)
                        ) ([], Set.add i visited)
                    
                    // Only create group if there are duplicates
                    let newGroups =
                        if List.isEmpty duplicateItems then
                            groups
                        else
                            let items = representative :: (duplicateItems |> List.map fst)
                            let avgSim =
                                if List.isEmpty duplicateItems then 1.0
                                else duplicateItems |> List.averageBy snd
                            {
                                Representative = representative
                                Items = Array.ofList items
                                AvgSimilarity = avgSim
                            } :: groups
                    
                    findGroups (i + 1) newVisited newGroups

            Ok (findGroups 0 Set.empty [] |> List.rev |> Array.ofList))
    
    // ========================================================================
    // CLUSTERING
    // ========================================================================
    
    /// Group items into clusters based on similarity
    let cluster
        (numClusters: int)
        (maxIterations: int)
        (index: SearchIndex<'T>)
        : QuantumResult<('T array) array> =
        
        if numClusters < 1 then
            Error (QuantumError.ValidationError ("Input", "Number of clusters must be at least 1"))
        elif numClusters > index.Items.Length then
            Error (QuantumError.Other "Number of clusters cannot exceed number of items")
        else
            // Simple k-means clustering
            let random = Random(42)
            let features = index.Items |> Array.map snd
            let n = features.Length
            let d = features.[0].Length
            
            // Initialize centroids randomly
            let initialCentroids =
                Array.init numClusters (fun _ -> features.[random.Next(n)])
            
            // Helper: Compute average centroid from cluster features
            let computeCentroid (clusterFeatures : float array array) =
                let count = Array.length clusterFeatures
                Array.init d (fun j ->
                    clusterFeatures 
                    |> Array.sumBy (fun (feature : float array) -> feature.[j])
                    |> fun sum -> sum / float count)
            
            // Helper: Assign each point to nearest centroid
            let assignClusters (centroids : float array array) =
                features
                |> Array.map (fun (feature : float array) ->
                    [| 0 .. numClusters - 1 |]
                    |> Array.maxBy (fun c -> cosineSimilarity feature centroids.[c]))
            
            // Helper: Update centroids based on assignments
            let updateCentroids (assignments : int array) (centroids : float array array) =
                [| 0 .. numClusters - 1 |]
                |> Array.map (fun c ->
                    let clusterFeatures =
                        features
                        |> Array.indexed
                        |> Array.filter (fun (i, _) -> assignments.[i] = c)
                        |> Array.map snd
                    if Array.isEmpty clusterFeatures 
                    then centroids.[c]
                    else computeCentroid clusterFeatures)
            
            // Recursive k-means iteration
            let rec kmeansIteration iter centroids prevAssignments =
                if iter >= maxIterations then
                    prevAssignments
                else
                    let newAssignments = assignClusters centroids
                    if newAssignments = prevAssignments then
                        newAssignments
                    else
                        let newCentroids = updateCentroids newAssignments centroids
                        kmeansIteration (iter + 1) newCentroids newAssignments
            
            let finalAssignments = 
                kmeansIteration 0 initialCentroids (Array.create n 0)
            
            // Group items by cluster
            let clusters =
                [| 0 .. numClusters - 1 |]
                |> Array.map (fun c ->
                    index.Items
                    |> Array.indexed
                    |> Array.filter (fun (i, _) -> finalAssignments.[i] = c)
                    |> Array.map (fun (_, (itemId, _)) -> itemId))
            
            Ok clusters
    
    // ========================================================================
    // PERSISTENCE
    // ========================================================================
    
    // ========================================================================
    // MODEL PERSISTENCE
    // ========================================================================
    
    /// Serializable index format (for JSON)
    /// Note: Generic items ('T) are not serialized - only feature vectors
    [<CLIMutable>]
    type private SerializableIndex = {
        /// Feature vectors only (items must be re-associated on load)
        Features: float array array
        
        /// Similarity metric
        Metric: string
        
        /// Threshold
        Threshold: float
        
        /// Precomputed kernel matrix (if quantum kernel was used)
        KernelMatrix: float array array option
        
        /// Number of items
        NumItems: int
        
        /// Number of features per item
        NumFeatures: int
        
        /// Created timestamp
        CreatedAt: string
        
        /// Optional note
        Note: string option
    }
    
    /// Save index to file (feature vectors only - items not serialized)
    let save (path: string) (index: SearchIndex<'T>) : QuantumResult<unit> =
        try
            let metricName =
                match index.Metric with
                | Cosine -> "Cosine"
                | Euclidean -> "Euclidean"
                | QuantumKernel -> "QuantumKernel"
            
            // Convert 2D kernel matrix to jagged array for JSON serialization
            let kernelArray =
                match index.KernelMatrix with
                | None -> None
                | Some matrix ->
                    let rows = Array2D.length1 matrix
                    let cols = Array2D.length2 matrix
                    Some [| for i in 0..rows-1 -> [| for j in 0..cols-1 -> matrix.[i,j] |] |]
            
            let serializable = {
                Features = index.Items |> Array.map snd
                Metric = metricName
                Threshold = index.Threshold
                KernelMatrix = kernelArray
                NumItems = index.Metadata.NumItems
                NumFeatures = index.Metadata.NumFeatures
                CreatedAt = index.Metadata.CreatedAt.ToString "o"
                Note = index.Metadata.Note
            }
            
            let options = JsonSerializerOptions(WriteIndented = true)
            let json = JsonSerializer.Serialize(serializable, options)
            File.WriteAllText(path, json)
            Ok ()
        with ex ->
            Error (QuantumError.ValidationError ("Input", $"Failed to save index: {ex.Message}"))
    
    /// Load index from file and re-associate with items
    ///
    /// IMPORTANT: Items ('T) are not serialized. You must provide the items
    /// in the same order as they were indexed. The function will pair them
    /// with the loaded feature vectors.
    ///
    /// Example:
    ///   let index = SimilaritySearch.load "index.json" products
    let loadWithItems (path: string) (items: 'T array) : QuantumResult<SearchIndex<'T>> =
        try
            if not (File.Exists path) then
                Error (QuantumError.ValidationError ("Input", $"File not found: {path}"))
            else
                let json = File.ReadAllText(path)
                let serializable = JsonSerializer.Deserialize<SerializableIndex>(json)
                
                // Validate items match feature vectors
                if items.Length <> serializable.Features.Length then
                    Error (QuantumError.ValidationError ("Input", $"Item count mismatch: provided {items.Length} items but index has {serializable.Features.Length} feature vectors"))
                else
                    // Parse metric
                    let metric =
                        match serializable.Metric with
                        | "Cosine" -> Cosine
                        | "Euclidean" -> Euclidean
                        | "QuantumKernel" -> QuantumKernel
                        | _ -> Cosine
                    
                    // Reconstruct 2D kernel matrix from jagged array
                    let kernelMatrix =
                        match serializable.KernelMatrix with
                        | None -> None
                        | Some jaggedArray ->
                            let rows = jaggedArray.Length
                            let cols = if rows > 0 then jaggedArray.[0].Length else 0
                            Some (Array2D.init rows cols (fun i j -> jaggedArray.[i].[j]))
                    
                    // Pair items with features
                    let pairedItems = Array.zip items serializable.Features
                    
                    Ok {
                        Items = pairedItems
                        Metric = metric
                        Threshold = serializable.Threshold
                        KernelMatrix = kernelMatrix
                        // Backends are not serialisable; a loaded index cannot run novel-query
                        // quantum-kernel searches and reports an honest error if asked to.
                        QuantumConfig = None
                        Metadata = {
                            NumItems = serializable.NumItems
                            NumFeatures = serializable.NumFeatures
                            Metric = metric
                            CreatedAt = DateTime.Parse(serializable.CreatedAt)
                            Note = serializable.Note
                        }
                    }
        with ex ->
            Error (QuantumError.ValidationError ("Input", $"Failed to load index: {ex.Message}"))
    
    /// Load index from file (deprecated - use loadWithItems instead)
    let load (path: string) : QuantumResult<SearchIndex<'T>> =
        Error (QuantumError.Other "Cannot load index without items. Use loadWithItems and provide the items array.")
    
    // ========================================================================
    // COMPUTATION EXPRESSION BUILDER
    // ========================================================================
    
    /// Computation expression builder for similarity search
    type SimilaritySearchBuilder<'T>() =
        
        member _.Yield(_) : SearchProblem<'T> =
            {
                Items = [||]
                Metric = Cosine
                Threshold = 0.7
                Backend = None
                Shots = 1000
                Verbose = false
                SavePath = None
                Note = None
                ProgressReporter = None
                CancellationToken = None
                Logger = None
            }
        
        member _.Delay(f: unit -> SearchProblem<'T>) = f
        
        member _.Run(f: unit -> SearchProblem<'T>) : QuantumResult<SearchIndex<'T>> =
            let problem = f()
            build problem
        
        member _.Combine(p1: SearchProblem<'T>, p2: SearchProblem<'T>) =
            { p2 with 
                Items = if p2.Items.Length = 0 then p1.Items else p2.Items
            }
        
        member _.Zero() : SearchProblem<'T> =
            {
                Items = [||]
                Metric = Cosine
                Threshold = 0.7
                Backend = None
                Shots = 1000
                Verbose = false
                SavePath = None
                Note = None
                ProgressReporter = None
                CancellationToken = None
                Logger = None
            }
        
        /// <summary>Set the items to index for similarity search.</summary>
        /// <param name="items">Array of (item, feature vector) pairs to index</param>
        [<CustomOperation("indexItems")>]
        member _.IndexItems(problem: SearchProblem<'T>, items: ('T * float array) array) =
            { problem with Items = items }
        
        /// <summary>Set the similarity metric for comparing vectors.</summary>
        /// <param name="metric">Similarity metric (Cosine, Euclidean, or Manhattan)</param>
        [<CustomOperation("similarityMetric")>]
        member _.SimilarityMetric(problem: SearchProblem<'T>, metric: SimilarityMetric) =
            { problem with Metric = metric }
        
        /// <summary>Set the similarity threshold for matching.</summary>
        /// <param name="threshold">Threshold value (0.0 to 1.0) for considering items similar</param>
        [<CustomOperation("threshold")>]
        member _.Threshold(problem: SearchProblem<'T>, threshold: float) =
            { problem with Threshold = threshold }
        
        /// <summary>Set the quantum backend for execution.</summary>
        /// <param name="backend">Quantum backend instance</param>
        [<CustomOperation("backend")>]
        member _.Backend(problem: SearchProblem<'T>, backend: IQuantumBackend) =
            { problem with Backend = Some backend }
        
        /// <summary>Set the number of measurement shots.</summary>
        /// <param name="shots">Number of circuit measurements</param>
        [<CustomOperation("shots")>]
        member _.Shots(problem: SearchProblem<'T>, shots: int) =
            { problem with Shots = shots }
        
        /// <summary>Enable or disable verbose output.</summary>
        /// <param name="verbose">True to enable detailed logging</param>
        [<CustomOperation("verbose")>]
        member _.Verbose(problem: SearchProblem<'T>, verbose: bool) =
            { problem with Verbose = verbose }
        
        /// <summary>Set the path to save the similarity index.</summary>
        /// <param name="path">File path for saving the index</param>
        [<CustomOperation("saveIndexTo")>]
        member _.SaveIndexTo(problem: SearchProblem<'T>, path: string) =
            { problem with SavePath = Some path }
        
        /// <summary>Add a note or description to the search problem.</summary>
        /// <param name="note">Descriptive note</param>
        [<CustomOperation("note")>]
        member _.Note(problem: SearchProblem<'T>, note: string) =
            { problem with Note = Some note }
        
        /// <summary>Set a progress reporter for real-time indexing updates.</summary>
        /// <param name="reporter">Progress reporter instance</param>
        [<CustomOperation("progressReporter")>]
        member _.ProgressReporter(problem: SearchProblem<'T>, reporter: Core.Progress.IProgressReporter) =
            { problem with ProgressReporter = Some reporter }
        
        /// <summary>Set a cancellation token for early termination of indexing.</summary>
        /// <param name="token">Cancellation token</param>
        [<CustomOperation("cancellationToken")>]
        member _.CancellationToken(problem: SearchProblem<'T>, token: System.Threading.CancellationToken) =
            { problem with CancellationToken = Some token }
    
    /// Create similarity search computation expression
    let similaritySearch<'T> = SimilaritySearchBuilder<'T>()
