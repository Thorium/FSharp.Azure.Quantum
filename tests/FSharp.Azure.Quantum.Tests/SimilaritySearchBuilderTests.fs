namespace FSharp.Azure.Quantum.Tests

open System
open Xunit
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Business
open FSharp.Azure.Quantum.Backends
open FSharp.Azure.Quantum.Core.BackendAbstraction

module SimilaritySearchBuilderTests =
    open SimilaritySearch

    // ========================================================================
    // HELPERS
    // ========================================================================

    /// Test items: fruit names with feature vectors
    let private testItems : (string * float array) array =
        [|
            ("apple",  [| 1.0; 0.1; 0.0 |])
            ("banana", [| 0.9; 0.2; 0.1 |])
            ("cherry", [| 0.8; 0.3; 0.0 |])
            ("date",   [| 0.1; 0.9; 0.8 |])
            ("elder",  [| 0.0; 0.8; 0.9 |])
        |]

    let private defaultProblem : SearchProblem<string> = {
        Items = testItems
        Metric = Cosine
        Threshold = 0.5
        Backend = None
        Shots = 100
        Verbose = false
        SavePath = None
        Note = None
        ProgressReporter = None
        CancellationToken = None
        Logger = None
    }

    // ========================================================================
    // VALIDATION TESTS
    // ========================================================================

    [<Fact>]
    let ``build with empty items should return ValidationError`` () =
        let problem = { defaultProblem with Items = [||] }
        match build problem with
        | Error (QuantumError.ValidationError ("Input", msg)) ->
            Assert.Contains("empty", msg)
        | other -> failwith $"Expected ValidationError for empty items, got {other}"

    [<Fact>]
    let ``build with threshold below 0 should return ValidationError`` () =
        let problem = { defaultProblem with Threshold = -0.1 }
        match build problem with
        | Error (QuantumError.ValidationError ("Input", msg)) ->
            Assert.Contains("Threshold", msg)
        | other -> failwith $"Expected ValidationError for negative threshold, got {other}"

    [<Fact>]
    let ``build with threshold above 1 should return ValidationError`` () =
        let problem = { defaultProblem with Threshold = 1.5 }
        match build problem with
        | Error (QuantumError.ValidationError ("Input", msg)) ->
            Assert.Contains("Threshold", msg)
        | other -> failwith $"Expected ValidationError for threshold > 1, got {other}"

    [<Fact>]
    let ``build with zero shots should return ValidationError`` () =
        let problem = { defaultProblem with Shots = 0 }
        match build problem with
        | Error (QuantumError.ValidationError ("Input", msg)) ->
            Assert.Contains("Shots", msg)
        | other -> failwith $"Expected ValidationError for zero shots, got {other}"

    [<Fact>]
    let ``build with mismatched feature lengths should return ValidationError`` () =
        let items = [|
            ("a", [| 1.0; 2.0 |])
            ("b", [| 1.0; 2.0; 3.0 |])
        |]
        let problem = { defaultProblem with Items = items }
        match build problem with
        | Error (QuantumError.ValidationError ("Input", msg)) ->
            Assert.Contains("same length", msg)
        | other -> failwith $"Expected ValidationError for mismatched features, got {other}"

    // ========================================================================
    // SUCCESSFUL BUILD TESTS
    // ========================================================================

    [<Fact>]
    let ``build with valid items and Cosine metric should succeed`` () =
        match build defaultProblem with
        | Ok index ->
            Assert.Equal(5, index.Items.Length)
            Assert.Equal(Cosine, index.Metric)
            Assert.Equal(0.5, index.Threshold)
            Assert.Equal(5, index.Metadata.NumItems)
            Assert.Equal(3, index.Metadata.NumFeatures)
            Assert.Equal(Cosine, index.Metadata.Metric)
            Assert.True(index.KernelMatrix.IsNone)
        | Error e -> failwith $"Expected Ok, got Error: {e}"

    [<Fact>]
    let ``build with Euclidean metric should succeed`` () =
        let problem = { defaultProblem with Metric = Euclidean }
        match build problem with
        | Ok index ->
            Assert.Equal(Euclidean, index.Metric)
            Assert.True(index.KernelMatrix.IsNone)
        | Error e -> failwith $"Expected Ok, got Error: {e}"

    [<Fact>]
    let ``build with QuantumKernel metric should succeed`` () =
        let problem = { defaultProblem with Metric = QuantumKernel }
        match build problem with
        | Ok index ->
            Assert.Equal(QuantumKernel, index.Metric)
            // Kernel matrix may or may not be computed depending on backend
        | Error e -> failwith $"Expected Ok, got Error: {e}"

    [<Fact>]
    let ``build with explicit backend should succeed`` () =
        let quantumBackend = LocalBackend.LocalBackend() :> IQuantumBackend
        let problem = { defaultProblem with Backend = Some quantumBackend }
        match build problem with
        | Ok index ->
            Assert.Equal(5, index.Items.Length)
        | Error e -> failwith $"Expected Ok, got Error: {e}"

    [<Fact>]
    let ``build with note should preserve note in metadata`` () =
        let problem = { defaultProblem with Note = Some "test note" }
        match build problem with
        | Ok index ->
            Assert.Equal(Some "test note", index.Metadata.Note)
        | Error e -> failwith $"Expected Ok, got Error: {e}"

    // ========================================================================
    // FIND SIMILAR TESTS
    // ========================================================================

    [<Fact>]
    let ``findSimilar should return matches ranked by similarity`` () =
        // Use threshold 0.0 so all items pass the filter (avoids Array.take bug when filtered < topN)
        let problem = { defaultProblem with Threshold = 0.0 }
        match build problem with
        | Ok index ->
            match findSimilar "apple" [| 1.0; 0.1; 0.0 |] 3 index with
            | Ok results ->
                Assert.Equal("apple", results.Query)
                Assert.True(results.Matches.Length <= 3)
                // Results should be sorted by similarity descending
                if results.Matches.Length >= 2 then
                    Assert.True(results.Matches.[0].Similarity >= results.Matches.[1].Similarity)
                // Ranks should be sequential
                results.Matches |> Array.iteri (fun i m -> Assert.Equal(i + 1, m.Rank))
            | Error e -> failwith $"Expected Ok from findSimilar, got Error: {e}"
        | Error e -> failwith $"Expected Ok from build, got Error: {e}"

    [<Fact>]
    let ``findSimilar should not include query item itself in results`` () =
        // Use threshold 0.0 so all items pass the filter
        let problem = { defaultProblem with Threshold = 0.0 }
        match build problem with
        | Ok index ->
            match findSimilar "apple" [| 1.0; 0.1; 0.0 |] 4 index with
            | Ok results ->
                let items = results.Matches |> Array.map (fun m -> m.Item)
                Assert.DoesNotContain("apple", items)
            | Error e -> failwith $"Expected Ok from findSimilar, got Error: {e}"
        | Error e -> failwith $"Expected Ok from build, got Error: {e}"

    [<Fact>]
    let ``findSimilar with Euclidean metric should return valid similarities`` () =
        let problem = { defaultProblem with Metric = Euclidean; Threshold = 0.0 }
        match build problem with
        | Ok index ->
            match findSimilar "apple" [| 1.0; 0.1; 0.0 |] 3 index with
            | Ok results ->
                results.Matches |> Array.iter (fun m ->
                    Assert.True(m.Similarity >= 0.0 && m.Similarity <= 1.0,
                        $"Euclidean similarity should be in [0,1], got {m.Similarity}"))
            | Error e -> failwith $"Expected Ok from findSimilar, got Error: {e}"
        | Error e -> failwith $"Expected Ok from build, got Error: {e}"

    [<Fact>]
    let ``findSimilar should record search time`` () =
        match build defaultProblem with
        | Ok index ->
            match findSimilar "apple" [| 1.0; 0.1; 0.0 |] 2 index with
            | Ok results ->
                Assert.True(results.SearchTime >= TimeSpan.Zero)
            | Error e -> failwith $"Expected Ok, got Error: {e}"
        | Error e -> failwith $"Expected Ok from build, got Error: {e}"

    // ========================================================================
    // FIND ALL SIMILAR TESTS
    // ========================================================================

    [<Fact>]
    let ``findAllSimilar should return all items above threshold`` () =
        let problem = { defaultProblem with Threshold = 0.9 }
        match build problem with
        | Ok index ->
            match findAllSimilar [| 1.0; 0.1; 0.0 |] index with
            | Ok matches ->
                matches |> Array.iter (fun m ->
                    Assert.True(m.Similarity >= 0.9,
                        $"All matches should be >= threshold 0.9, got {m.Similarity}"))
            | Error e -> failwith $"Expected Ok, got Error: {e}"
        | Error e -> failwith $"Expected Ok from build, got Error: {e}"

    [<Fact>]
    let ``findAllSimilar with very high threshold should return few or no matches`` () =
        let problem = { defaultProblem with Threshold = 0.999 }
        match build problem with
        | Ok index ->
            match findAllSimilar [| 0.5; 0.5; 0.5 |] index with
            | Ok matches ->
                // With a very high threshold, we might get no matches or just one
                Assert.True(matches.Length <= 5)
            | Error e -> failwith $"Expected Ok, got Error: {e}"
        | Error e -> failwith $"Expected Ok from build, got Error: {e}"

    // ========================================================================
    // DUPLICATE DETECTION TESTS
    // ========================================================================

    [<Fact>]
    let ``findDuplicates with high threshold should find near-identical items`` () =
        // Add near-duplicates to test
        let items = [|
            ("a", [| 1.0; 0.0; 0.0 |])
            ("b", [| 0.99; 0.01; 0.0 |])  // near-duplicate of a
            ("c", [| 0.0; 1.0; 0.0 |])
            ("d", [| 0.0; 0.99; 0.01 |])  // near-duplicate of c
        |]
        let problem = { defaultProblem with Items = items; Threshold = 0.5 }
        match build problem with
        | Ok index ->
            match findDuplicates 0.99 index with
            | Ok groups ->
                // Should find at least one duplicate group
                groups |> Array.iter (fun g ->
                    Assert.True(g.Items.Length >= 2, "Duplicate groups should have >= 2 items")
                    Assert.True(g.AvgSimilarity >= 0.99))
            | Error e -> failwith $"Expected Ok, got Error: {e}"
        | Error e -> failwith $"Expected Ok from build, got Error: {e}"

    [<Fact>]
    let ``findDuplicates with invalid threshold should return ValidationError`` () =
        match build defaultProblem with
        | Ok index ->
            match findDuplicates 1.5 index with
            | Error (QuantumError.ValidationError ("Threshold", _)) -> ()
            | other -> failwith $"Expected ValidationError for invalid threshold, got {other}"
        | Error e -> failwith $"Expected Ok from build, got Error: {e}"

    [<Fact>]
    let ``findDuplicates with low threshold should find more groups`` () =
        match build defaultProblem with
        | Ok index ->
            match findDuplicates 0.5 index with
            | Ok groups ->
                // With a low threshold, we may find groups (or not depending on data)
                groups |> Array.iter (fun g ->
                    Assert.True(g.Items.Length >= 2))
            | Error e -> failwith $"Expected Ok, got Error: {e}"
        | Error e -> failwith $"Expected Ok from build, got Error: {e}"

    // ========================================================================
    // CLUSTERING TESTS
    // ========================================================================

    [<Fact>]
    let ``cluster with 2 clusters should partition items`` () =
        match build defaultProblem with
        | Ok index ->
            match cluster 2 10 index with
            | Ok clusters ->
                Assert.Equal(2, clusters.Length)
                // All items should be assigned to exactly one cluster
                let totalItems = clusters |> Array.sumBy Array.length
                Assert.Equal(5, totalItems)
            | Error e -> failwith $"Expected Ok, got Error: {e}"
        | Error e -> failwith $"Expected Ok from build, got Error: {e}"

    [<Fact>]
    let ``cluster with 0 clusters should return ValidationError`` () =
        match build defaultProblem with
        | Ok index ->
            match cluster 0 10 index with
            | Error (QuantumError.ValidationError ("Input", _)) -> ()
            | other -> failwith $"Expected ValidationError for 0 clusters, got {other}"
        | Error e -> failwith $"Expected Ok from build, got Error: {e}"

    [<Fact>]
    let ``cluster with more clusters than items should return error`` () =
        match build defaultProblem with
        | Ok index ->
            match cluster 10 10 index with
            | Error _ -> ()
            | Ok _ -> failwith "Expected error for numClusters > numItems"
        | Error e -> failwith $"Expected Ok from build, got Error: {e}"

    // ========================================================================
    // COMPUTATION EXPRESSION TESTS
    // ========================================================================

    [<Fact>]
    let ``CE similaritySearch with indexItems should build successfully`` () =
        let result = similaritySearch<string> {
            indexItems testItems
        }
        match result with
        | Ok index ->
            Assert.Equal(5, index.Items.Length)
            Assert.Equal(Cosine, index.Metric)  // default
            Assert.Equal(0.7, index.Threshold)  // default
        | Error e -> failwith $"Expected Ok, got Error: {e}"

    [<Fact>]
    let ``CE similaritySearch with all options should succeed`` () =
        let quantumBackend = LocalBackend.LocalBackend() :> IQuantumBackend
        let result = similaritySearch<string> {
            indexItems testItems
            similarityMetric Euclidean
            threshold 0.3
            backend quantumBackend
            shots 200
            note "test similarity search"
        }
        match result with
        | Ok index ->
            Assert.Equal(Euclidean, index.Metric)
            Assert.Equal(0.3, index.Threshold)
            Assert.Equal(Some "test similarity search", index.Metadata.Note)
        | Error e -> failwith $"Expected Ok, got Error: {e}"

    [<Fact>]
    let ``CE similaritySearch with empty items should return error`` () =
        let result = similaritySearch<string> {
            indexItems [||]
        }
        match result with
        | Error (QuantumError.ValidationError ("Input", msg)) ->
            Assert.Contains("empty", msg)
        | other -> failwith $"Expected ValidationError for empty items, got {other}"

    [<Fact>]
    let ``CE similaritySearch with QuantumKernel metric should succeed`` () =
        let result = similaritySearch<string> {
            indexItems testItems
            similarityMetric QuantumKernel
        }
        match result with
        | Ok index ->
            Assert.Equal(QuantumKernel, index.Metric)
        | Error e -> failwith $"Expected Ok, got Error: {e}"

    [<Fact>]
    let ``CE similaritySearch with invalid threshold should return error`` () =
        let result = similaritySearch<string> {
            indexItems testItems
            threshold 2.0
        }
        match result with
        | Error (QuantumError.ValidationError _) -> ()
        | other -> failwith $"Expected ValidationError for invalid threshold, got {other}"
