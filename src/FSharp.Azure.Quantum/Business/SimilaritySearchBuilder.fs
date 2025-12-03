namespace FSharp.Azure.Quantum.Business

open System
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.MachineLearning

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
        | Cosine         // Cosine similarity (default)
        | Euclidean      // Euclidean distance
        | QuantumKernel  // Quantum kernel similarity
    
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
    let private validate (problem: SearchProblem<'T>) : Result<unit, string> =
        if problem.Items.Length = 0 then
            Error "Items cannot be empty"
        elif problem.Threshold < 0.0 || problem.Threshold > 1.0 then
            Error "Threshold must be between 0.0 and 1.0"
        elif problem.Shots < 1 then
            Error "Shots must be at least 1"
        else
            let numFeatures = snd problem.Items.[0] |> Array.length
            let allSameLength = 
                problem.Items 
                |> Array.forall (fun (_, features) -> features.Length = numFeatures)
            
            if not allSameLength then
                Error "All feature vectors must have the same length"
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
        : Result<float, string> =
        
        QuantumKernels.computeKernel backend featureMap a b shots
    
    // ========================================================================
    // INDEX BUILDING
    // ========================================================================
    
    /// Build similarity search index
    let build (problem: SearchProblem<'T>) : Result<SearchIndex<'T>, string> =
        match validate problem with
        | Error e -> Error e
        | Ok () ->
            
            let startTime = DateTime.UtcNow
            let numFeatures = snd problem.Items.[0] |> Array.length
            
            if problem.Verbose then
                printfn "Building similarity index..."
                printfn "  Items: %d" problem.Items.Length
                printfn "  Features: %d" numFeatures
                printfn "  Metric: %A" problem.Metric
            
            // Precompute kernel matrix if using quantum kernel
            let kernelMatrix =
                match problem.Metric with
                | QuantumKernel ->
                    
                    let backend = 
                        match problem.Backend with
                        | Some b -> b
                        | None -> LocalBackend() :> IQuantumBackend
                    
                    let numQubits = min numFeatures 8
                    let featureMap = FeatureMapType.ZZFeatureMap 2
                    let features = problem.Items |> Array.map snd
                    
                    if problem.Verbose then
                        printfn "  Computing quantum kernel matrix..."
                    
                    match QuantumKernels.computeKernelMatrix backend featureMap features problem.Shots with
                    | Ok matrix -> Some matrix
                    | Error e ->
                        if problem.Verbose then
                            printfn "  ⚠️  Warning: Kernel computation failed: %s" e
                            printfn "  Falling back to cosine similarity"
                        None
                
                | _ -> None
            
            let endTime = DateTime.UtcNow
            
            if problem.Verbose then
                printfn "✅ Index built in %A" (endTime - startTime)
            
            let index = {
                Items = problem.Items
                Metric = problem.Metric
                Threshold = problem.Threshold
                KernelMatrix = kernelMatrix
                Metadata = {
                    NumItems = problem.Items.Length
                    NumFeatures = numFeatures
                    Metric = problem.Metric
                    CreatedAt = startTime
                    Note = problem.Note
                }
            }
            
            Ok index
    
    // ========================================================================
    // SIMILARITY SEARCH
    // ========================================================================
    
    /// Find top N most similar items
    let findSimilar 
        (queryItem: 'T)
        (queryFeatures: float array)
        (topN: int)
        (index: SearchIndex<'T>)
        : Result<SearchResults<'T>, string> =
        
        let startTime = DateTime.UtcNow
        
        // Find query index if item is in index
        let queryIdx = 
            index.Items 
            |> Array.tryFindIndex (fun (item, _) -> obj.Equals(item, queryItem))
        
        // Compute similarities
        let similarities =
            match index.Metric, index.KernelMatrix, queryIdx with
            | QuantumKernel, Some matrix, Some idx ->
                // Use precomputed kernel matrix
                [| for i in 0 .. index.Items.Length - 1 ->
                    if i = idx then
                        (fst index.Items.[i], 1.0)  // Self-similarity is 1.0
                    else
                        (fst index.Items.[i], matrix.[idx, i])
                |]
            
            | QuantumKernel, None, _ ->
                // Fallback to cosine if kernel not available
                index.Items
                |> Array.map (fun (item, features) ->
                    (item, cosineSimilarity queryFeatures features))
            
            | Cosine, _, _ ->
                index.Items
                |> Array.map (fun (item, features) ->
                    (item, cosineSimilarity queryFeatures features))
            
            | Euclidean, _, _ ->
                index.Items
                |> Array.map (fun (item, features) ->
                    (item, euclideanSimilarity queryFeatures features))
        
        // Filter by threshold and exclude exact match
        let filtered =
            similarities
            |> Array.filter (fun (item, sim) ->
                sim >= index.Threshold && not (obj.Equals(item, queryItem)))
            |> Array.sortByDescending snd
            |> Array.take (min topN (Array.length similarities - 1))
        
        let matches =
            filtered
            |> Array.mapi (fun i (item, sim) ->
                {
                    Item = item
                    Similarity = sim
                    Rank = i + 1
                })
        
        let endTime = DateTime.UtcNow
        
        Ok {
            Query = queryItem
            Matches = matches
            SearchTime = endTime - startTime
        }
    
    /// Find all similar items above threshold
    let findAllSimilar
        (queryFeatures: float array)
        (index: SearchIndex<'T>)
        : Result<Match<'T> array, string> =
        
        // Compute similarities for all items
        let similarities =
            match index.Metric with
            | Cosine ->
                index.Items
                |> Array.map (fun (item, features) ->
                    (item, cosineSimilarity queryFeatures features))
            
            | Euclidean ->
                index.Items
                |> Array.map (fun (item, features) ->
                    (item, euclideanSimilarity queryFeatures features))
            
            | QuantumKernel ->
                // Use cosine as fallback for single query
                index.Items
                |> Array.map (fun (item, features) ->
                    (item, cosineSimilarity queryFeatures features))
        
        let matches =
            similarities
            |> Array.filter (fun (_, sim) -> sim >= index.Threshold)
            |> Array.sortByDescending snd
            |> Array.mapi (fun i (item, sim) ->
                {
                    Item = item
                    Similarity = sim
                    Rank = i + 1
                })
        
        Ok matches
    
    // ========================================================================
    // DUPLICATE DETECTION
    // ========================================================================
    
    /// Find duplicate groups (items similar to each other)
    let findDuplicates
        (threshold: float)
        (index: SearchIndex<'T>)
        : Result<DuplicateGroup<'T> array, string> =
        
        if threshold < 0.0 || threshold > 1.0 then
            Error "Threshold must be between 0.0 and 1.0"
        else
            // Compute all pairwise similarities
            let n = index.Items.Length
            let visited = Array.create n false
            let groups = ResizeArray<DuplicateGroup<'T>>()
            
            for i in 0 .. n - 1 do
                if not visited.[i] then
                    let (representative, repFeatures) = index.Items.[i]
                    let duplicates = ResizeArray<'T>()
                    let similarities = ResizeArray<float>()
                    
                    duplicates.Add(representative)
                    visited.[i] <- true
                    
                    // Find all items similar to representative
                    for j in (i + 1) .. n - 1 do
                        if not visited.[j] then
                            let (item, features) = index.Items.[j]
                            
                            let similarity =
                                match index.Metric with
                                | Cosine -> cosineSimilarity repFeatures features
                                | Euclidean -> euclideanSimilarity repFeatures features
                                | QuantumKernel ->
                                    match index.KernelMatrix with
                                    | Some matrix -> matrix.[i, j]
                                    | None -> cosineSimilarity repFeatures features
                            
                            if similarity >= threshold then
                                duplicates.Add(item)
                                similarities.Add(similarity)
                                visited.[j] <- true
                    
                    // Only create group if there are duplicates
                    if duplicates.Count > 1 then
                        let avgSim = 
                            if similarities.Count > 0 then
                                similarities |> Seq.average
                            else
                                1.0
                        
                        groups.Add({
                            Representative = representative
                            Items = duplicates.ToArray()
                            AvgSimilarity = avgSim
                        })
            
            Ok (groups.ToArray())
    
    // ========================================================================
    // CLUSTERING
    // ========================================================================
    
    /// Group items into clusters based on similarity
    let cluster
        (numClusters: int)
        (maxIterations: int)
        (index: SearchIndex<'T>)
        : Result<('T array) array, string> =
        
        if numClusters < 1 then
            Error "Number of clusters must be at least 1"
        elif numClusters > index.Items.Length then
            Error "Number of clusters cannot exceed number of items"
        else
            // Simple k-means clustering
            let random = Random(42)
            let features = index.Items |> Array.map snd
            let n = features.Length
            let d = features.[0].Length
            
            // Initialize centroids randomly
            let mutable centroids =
                [| for _ in 1 .. numClusters ->
                    features.[random.Next(n)]
                |]
            
            let mutable assignments = Array.create n 0
            let mutable changed = true
            let mutable iter = 0
            
            while changed && iter < maxIterations do
                changed <- false
                iter <- iter + 1
                
                // Assignment step
                for i in 0 .. n - 1 do
                    let bestCluster =
                        [| 0 .. numClusters - 1 |]
                        |> Array.maxBy (fun c ->
                            cosineSimilarity features.[i] centroids.[c])
                    
                    if assignments.[i] <> bestCluster then
                        assignments.[i] <- bestCluster
                        changed <- true
                
                // Update centroids
                for c in 0 .. numClusters - 1 do
                    let clusterItems =
                        [| 0 .. n - 1 |]
                        |> Array.filter (fun i -> assignments.[i] = c)
                    
                    if clusterItems.Length > 0 then
                        let newCentroid = Array.create d 0.0
                        for i in clusterItems do
                            for j in 0 .. d - 1 do
                                newCentroid.[j] <- newCentroid.[j] + features.[i].[j]
                        
                        for j in 0 .. d - 1 do
                            newCentroid.[j] <- newCentroid.[j] / float clusterItems.Length
                        
                        centroids.[c] <- newCentroid
            
            // Group items by cluster
            let clusters =
                [| for c in 0 .. numClusters - 1 ->
                    [| 0 .. n - 1 |]
                    |> Array.filter (fun i -> assignments.[i] = c)
                    |> Array.map (fun i -> fst index.Items.[i])
                |]
            
            Ok clusters
    
    // ========================================================================
    // PERSISTENCE
    // ========================================================================
    
    /// Save index to file
    let save (path: string) (index: SearchIndex<'T>) : Result<unit, string> =
        Error "Index persistence not yet implemented"
    
    /// Load index from file
    let load (path: string) : Result<SearchIndex<'T>, string> =
        Error "Index loading not yet implemented"
    
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
            }
        
        member _.Delay(f: unit -> SearchProblem<'T>) = f
        
        member _.Run(f: unit -> SearchProblem<'T>) : Result<SearchIndex<'T>, string> =
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
            }
        
        [<CustomOperation("indexItems")>]
        member _.IndexItems(problem: SearchProblem<'T>, items: ('T * float array) array) =
            { problem with Items = items }
        
        [<CustomOperation("similarityMetric")>]
        member _.SimilarityMetric(problem: SearchProblem<'T>, metric: SimilarityMetric) =
            { problem with Metric = metric }
        
        [<CustomOperation("threshold")>]
        member _.Threshold(problem: SearchProblem<'T>, threshold: float) =
            { problem with Threshold = threshold }
        
        [<CustomOperation("backend")>]
        member _.Backend(problem: SearchProblem<'T>, backend: IQuantumBackend) =
            { problem with Backend = Some backend }
        
        [<CustomOperation("shots")>]
        member _.Shots(problem: SearchProblem<'T>, shots: int) =
            { problem with Shots = shots }
        
        [<CustomOperation("verbose")>]
        member _.Verbose(problem: SearchProblem<'T>, verbose: bool) =
            { problem with Verbose = verbose }
        
        [<CustomOperation("saveIndexTo")>]
        member _.SaveIndexTo(problem: SearchProblem<'T>, path: string) =
            { problem with SavePath = Some path }
        
        [<CustomOperation("note")>]
        member _.Note(problem: SearchProblem<'T>, note: string) =
            { problem with Note = Some note }
    
    /// Create similarity search computation expression
    let similaritySearch<'T> = SimilaritySearchBuilder<'T>()
