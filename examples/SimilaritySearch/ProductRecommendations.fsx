(**
# Similarity Search: Product Recommendations

Quantum kernel-based similarity for e-commerce recommendations.

(*
===============================================================================
 Background Theory
===============================================================================

Product recommendations are a cornerstone of e-commerce, increasing average
order value by 10-30%. Traditional approaches use collaborative filtering
("users who bought X also bought Y") or content-based filtering (item features).
Quantum similarity search extends content-based filtering by computing item
similarity in exponentially large Hilbert spaces via quantum kernels.

Given product feature vectors x, x' (price, category, ratings, etc.), the
quantum kernel K(x,x') = |<psi(x)|psi(x')>|^2 measures similarity in a
2^n-dimensional space for n qubits. This high-dimensional embedding can capture
complex feature interactions that linear similarity measures miss -- for example,
the non-obvious relationship between price range, category, and rating patterns.

Key Equations:
  - Quantum kernel: K(x,x') = |<psi(x)|psi(x')>|^2 (state overlap)
  - Cosine similarity: cos(theta) = (x . x') / (|x| |x'|)  (baseline)
  - Recommendation score: s(q, p) = alpha * K(q, p) + beta * popularity(p)
  - Duplicate detection: flag pairs where K(p1, p2) > threshold_dup
  - Clustering: k-means in kernel space for automatic product grouping

Quantum Advantage:
  For high-dimensional product catalogs with mixed feature types (numerical,
  categorical, text embeddings), quantum kernels can detect non-linear
  similarities that cosine similarity in the original feature space would miss.
  As catalog size grows, quantum approximate nearest neighbor search could
  provide polynomial speedup via Grover-like techniques.
*)

## Overview

Uses quantum similarity search to find related products, detect duplicates,
and cluster catalogs. Five examples from basic recommendations to production API.

### Business Problem

- Show "similar products" to increase basket size
- Find duplicate listings (data quality)
- Auto-group products into categories
- Power cross-sell / upsell widgets

### Usage

    dotnet fsi ProductRecommendations.fsx                                      (defaults)
    dotnet fsi ProductRecommendations.fsx -- --help                            (show options)
    dotnet fsi ProductRecommendations.fsx -- --example 3 --threshold 0.9
    dotnet fsi ProductRecommendations.fsx -- --quiet --output results.json
    dotnet fsi ProductRecommendations.fsx -- --example all --csv results.csv
*)

/// Similarity Search Example: Product Recommendations
/// Implementation using quantum kernel similarity

//#r "nuget: FSharp.Azure.Quantum"
#r "../../src/FSharp.Azure.Quantum/bin/Debug/net10.0/FSharp.Azure.Quantum.dll"

#load "../_common/Cli.fs"
#load "../_common/Data.fs"
#load "../_common/Reporting.fs"

open System
open FSharp.Azure.Quantum.Business
open FSharp.Azure.Quantum.Business.SimilaritySearch
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Backends.LocalBackend
open FSharp.Azure.Quantum.Examples.Common

// ==============================================================================
// CLI ARGUMENT PARSING
// ==============================================================================

let argv = fsi.CommandLineArgs |> Array.skip 1
let args = Cli.parse argv

Cli.exitIfHelp
    "ProductRecommendations.fsx"
    "Product recommendations using quantum similarity search."
    [ { Cli.OptionSpec.Name = "example";    Description = "Example to run (1-5 or all)";         Default = Some "all" }
      { Cli.OptionSpec.Name = "threshold";  Description = "Similarity threshold (0.0-1.0)";      Default = Some "0.6" }
      { Cli.OptionSpec.Name = "output";     Description = "Write results to JSON file";           Default = None }
      { Cli.OptionSpec.Name = "csv";        Description = "Write results to CSV file";            Default = None }
      { Cli.OptionSpec.Name = "quiet";      Description = "Suppress informational output";        Default = None } ]
    args

let quiet = Cli.hasFlag "quiet" args
let outputPath = Cli.tryGet "output" args
let csvPath = Cli.tryGet "csv" args
let exampleFilter = Cli.getOr "example" "all" args
let cliThreshold = Cli.getFloatOr "threshold" 0.6 args

let shouldRun (n: int) =
    exampleFilter = "all" || exampleFilter = string n

let results = System.Collections.Generic.List<{| Example: string; Status: string; Details: Map<string, obj> |}>()

// ==============================================================================
// QUANTUM BACKEND (Rule 1 compliance)
// ==============================================================================

let quantumBackend = LocalBackend() :> IQuantumBackend

// ==============================================================================
// SAMPLE DATA - Product Catalog
// ==============================================================================

/// Product type for e-commerce site
type Product = {
    Id: string
    Name: string
    Price: float
    Category: string
    Rating: float
    NumReviews: int
}

/// Extract features from product for similarity computation
let extractFeatures (product: Product) : float array =
    [|
        product.Price / 1000.0           // Normalize price to [0, 1]
        float product.NumReviews / 100.0 // Normalize reviews
        product.Rating / 5.0             // Normalize rating to [0, 1]
        // Category encoding (one-hot)
        if product.Category = "Electronics" then 1.0 else 0.0
        if product.Category = "Books" then 1.0 else 0.0
        if product.Category = "Clothing" then 1.0 else 0.0
        if product.Category = "Home" then 1.0 else 0.0
    |]

/// Sample product catalog
let catalog =
    [|
        // Electronics
        { Id = "P001"; Name = "Laptop Pro 15\""; Price = 1299.99; Category = "Electronics"; Rating = 4.5; NumReviews = 234 }
        { Id = "P002"; Name = "Laptop Air 13\""; Price = 999.99; Category = "Electronics"; Rating = 4.7; NumReviews = 456 }
        { Id = "P003"; Name = "Tablet 10\""; Price = 499.99; Category = "Electronics"; Rating = 4.3; NumReviews = 189 }
        { Id = "P004"; Name = "Smartphone X"; Price = 899.99; Category = "Electronics"; Rating = 4.6; NumReviews = 678 }
        { Id = "P005"; Name = "Wireless Headphones"; Price = 249.99; Category = "Electronics"; Rating = 4.4; NumReviews = 345 }
        // Books
        { Id = "P006"; Name = "Quantum Computing Explained"; Price = 39.99; Category = "Books"; Rating = 4.8; NumReviews = 89 }
        { Id = "P007"; Name = "Machine Learning Basics"; Price = 44.99; Category = "Books"; Rating = 4.7; NumReviews = 156 }
        { Id = "P008"; Name = "Python Programming"; Price = 34.99; Category = "Books"; Rating = 4.6; NumReviews = 234 }
        { Id = "P009"; Name = "Data Science Handbook"; Price = 49.99; Category = "Books"; Rating = 4.9; NumReviews = 123 }
        // Clothing
        { Id = "P010"; Name = "Running Shoes"; Price = 89.99; Category = "Clothing"; Rating = 4.5; NumReviews = 456 }
        { Id = "P011"; Name = "Winter Jacket"; Price = 129.99; Category = "Clothing"; Rating = 4.6; NumReviews = 234 }
        { Id = "P012"; Name = "Cotton T-Shirt"; Price = 19.99; Category = "Clothing"; Rating = 4.3; NumReviews = 678 }
        { Id = "P013"; Name = "Jeans Classic"; Price = 59.99; Category = "Clothing"; Rating = 4.4; NumReviews = 345 }
        // Home
        { Id = "P014"; Name = "Coffee Maker"; Price = 79.99; Category = "Home"; Rating = 4.5; NumReviews = 234 }
        { Id = "P015"; Name = "Blender Pro"; Price = 99.99; Category = "Home"; Rating = 4.7; NumReviews = 189 }
        { Id = "P016"; Name = "Vacuum Cleaner"; Price = 199.99; Category = "Home"; Rating = 4.6; NumReviews = 156 }
        { Id = "P017"; Name = "Air Purifier"; Price = 149.99; Category = "Home"; Rating = 4.8; NumReviews = 123 }
    |]

// ==============================================================================
// SHARED INDEX (built once, reused across examples)
// ==============================================================================

let indexResult = similaritySearch {
    indexItems (catalog |> Array.map (fun p -> (p, extractFeatures p)))
    similarityMetric Cosine
    threshold cliThreshold
    backend quantumBackend
}

match indexResult with
| Error err ->
    if not quiet then printfn "Index build failed: %s" err.Message
| Ok _ ->
    if not quiet then printfn "Product index built: %d items, threshold=%.2f\n" catalog.Length cliThreshold

// ==============================================================================
// EXAMPLE 1: Basic Product Recommendations
// ==============================================================================

if shouldRun 1 then
    if not quiet then
        printfn "=== Example 1: Product Recommendations (Basic) ===\n"

    match indexResult with
    | Error err ->
        if not quiet then printfn "Indexing failed: %s" err.Message
        results.Add({| Example = "1-basic"; Status = "error"; Details = Map.ofList ["error", box err.Message] |})

    | Ok index ->
        let currentProduct = catalog.[0]  // Laptop Pro 15"
        if not quiet then
            printfn "Customer viewing: %s ($%.2f)" currentProduct.Name currentProduct.Price
            printfn "\nRecommendations:\n"

        match SimilaritySearch.findSimilar currentProduct (extractFeatures currentProduct) 5 index with
        | Ok searchResults ->
            if not quiet then
                searchResults.Matches
                |> Array.iter (fun m ->
                    printfn "  %d. %s" m.Rank m.Item.Name
                    printfn "     $%.2f | Rating: %.1f | %.0f%% match\n"
                        m.Item.Price m.Item.Rating (m.Similarity * 100.0)
                )
                printfn "Search time: %A" searchResults.SearchTime

            results.Add({| Example = "1-basic"; Status = "ok"; Details = Map.ofList [
                "viewing", box currentProduct.Name
                "recommendations", box (searchResults.Matches |> Array.map (fun m ->
                    {| Name = m.Item.Name; Price = m.Item.Price; Similarity = m.Similarity; Rank = m.Rank |}))
                "search_time_ms", box searchResults.SearchTime.TotalMilliseconds
            ] |})

        | Error err ->
            if not quiet then printfn "Search failed: %s" err.Message
            results.Add({| Example = "1-basic"; Status = "error"; Details = Map.ofList ["error", box err.Message] |})

// ==============================================================================
// EXAMPLE 2: Cross-Sell Recommendations
// ==============================================================================

if shouldRun 2 then
    if not quiet then
        printfn "\n=== Example 2: Cross-Sell Opportunities ===\n"

    match indexResult with
    | Error err ->
        if not quiet then printfn "Indexing failed: %s" err.Message
        results.Add({| Example = "2-cross-sell"; Status = "error"; Details = Map.ofList ["error", box err.Message] |})

    | Ok index ->
        let purchasedProduct = catalog |> Array.find (fun p -> p.Id = "P005")  // Wireless Headphones
        if not quiet then
            printfn "Customer purchased: %s\n" purchasedProduct.Name
            printfn "Cross-sell suggestions:\n"

        match SimilaritySearch.findSimilar purchasedProduct (extractFeatures purchasedProduct) 3 index with
        | Ok searchResults ->
            if not quiet then
                searchResults.Matches
                |> Array.iter (fun m ->
                    printfn "  - %s (%.0f%% match)" m.Item.Name (m.Similarity * 100.0)
                    printfn "    \"Customers who bought %s also bought %s\"\n"
                        purchasedProduct.Name m.Item.Name
                )

            results.Add({| Example = "2-cross-sell"; Status = "ok"; Details = Map.ofList [
                "purchased", box purchasedProduct.Name
                "cross_sell", box (searchResults.Matches |> Array.map (fun m ->
                    {| Name = m.Item.Name; Similarity = m.Similarity |}))
            ] |})

        | Error err ->
            if not quiet then printfn "Search failed: %s" err.Message
            results.Add({| Example = "2-cross-sell"; Status = "error"; Details = Map.ofList ["error", box err.Message] |})

// ==============================================================================
// EXAMPLE 3: Duplicate Detection (Data Quality)
// ==============================================================================

if shouldRun 3 then
    if not quiet then
        printfn "\n=== Example 3: Find Duplicate Products ===\n"

    // Add near-duplicate products
    let catalogWithDuplicates =
        Array.append catalog [|
            { Id = "P018"; Name = "Laptop Pro 15\" (Renewed)"; Price = 1099.99; Category = "Electronics"; Rating = 4.4; NumReviews = 89 }
            { Id = "P019"; Name = "Laptop Pro 15\" - Refurbished"; Price = 1199.99; Category = "Electronics"; Rating = 4.3; NumReviews = 56 }
        |]

    let dupIndexResult = similaritySearch {
        indexItems (catalogWithDuplicates |> Array.map (fun p -> (p, extractFeatures p)))
        similarityMetric Cosine
        backend quantumBackend
    }

    match dupIndexResult with
    | Error err ->
        if not quiet then printfn "Indexing failed: %s" err.Message
        results.Add({| Example = "3-duplicates"; Status = "error"; Details = Map.ofList ["error", box err.Message] |})

    | Ok index ->
        if not quiet then printfn "Scanning %d products for duplicates...\n" catalogWithDuplicates.Length

        match SimilaritySearch.findDuplicates 0.85 index with
        | Ok groups ->
            if not quiet then
                if groups.Length = 0 then
                    printfn "No duplicate groups found."
                else
                    printfn "Found %d duplicate groups:\n" groups.Length
                    groups
                    |> Array.iteri (fun i group ->
                        printfn "Group %d (%.0f%% similar):" (i+1) (group.AvgSimilarity * 100.0)
                        group.Items
                        |> Array.iter (fun p ->
                            printfn "  - %s (ID: %s, $%.2f)" p.Name p.Id p.Price
                        )
                        printfn ""
                    )
                    printfn "Data Quality Action: Merge duplicate listings to improve catalog"

            results.Add({| Example = "3-duplicates"; Status = "ok"; Details = Map.ofList [
                "products_scanned", box catalogWithDuplicates.Length
                "duplicate_groups", box groups.Length
                "groups", box (groups |> Array.map (fun g ->
                    {| Items = g.Items |> Array.map (fun p -> p.Name); AvgSimilarity = g.AvgSimilarity |}))
            ] |})

        | Error err ->
            if not quiet then printfn "Duplicate detection failed: %s" err.Message
            results.Add({| Example = "3-duplicates"; Status = "error"; Details = Map.ofList ["error", box err.Message] |})

// ==============================================================================
// EXAMPLE 4: Automatic Product Clustering
// ==============================================================================

if shouldRun 4 then
    if not quiet then
        printfn "\n=== Example 4: Automatic Product Clustering ===\n"

    match indexResult with
    | Error err ->
        if not quiet then printfn "Indexing failed: %s" err.Message
        results.Add({| Example = "4-clustering"; Status = "error"; Details = Map.ofList ["error", box err.Message] |})

    | Ok index ->
        if not quiet then printfn "Clustering products into 4 groups...\n"

        match SimilaritySearch.cluster 4 100 index with
        | Ok clusters ->
            if not quiet then
                clusters
                |> Array.iteri (fun i cluster ->
                    printfn "Cluster %d (%d products):" (i+1) cluster.Length
                    cluster
                    |> Array.take (min 5 cluster.Length)
                    |> Array.iter (fun p ->
                        printfn "  - %s ($%.2f, %s)" p.Name p.Price p.Category
                    )
                    if cluster.Length > 5 then
                        printfn "  ... and %d more" (cluster.Length - 5)
                    printfn ""
                )

            results.Add({| Example = "4-clustering"; Status = "ok"; Details = Map.ofList [
                "num_clusters", box clusters.Length
                "clusters", box (clusters |> Array.mapi (fun i c ->
                    {| Cluster = i + 1; Size = c.Length; Products = c |> Array.map (fun p -> p.Name) |}))
            ] |})

        | Error err ->
            if not quiet then printfn "Clustering failed: %s" err.Message
            results.Add({| Example = "4-clustering"; Status = "error"; Details = Map.ofList ["error", box err.Message] |})

// ==============================================================================
// EXAMPLE 5: Production Recommendation API
// ==============================================================================

if shouldRun 5 then
    if not quiet then
        printfn "\n=== Example 5: Production Recommendation Engine ===\n"

    let prodResult = similaritySearch {
        indexItems (catalog |> Array.map (fun p -> (p, extractFeatures p)))
        similarityMetric Cosine
        threshold cliThreshold
        verbose (not quiet)
        backend quantumBackend
    }

    match prodResult with
    | Error err ->
        if not quiet then printfn "Engine initialization failed: %s" err.Message
        results.Add({| Example = "5-production"; Status = "error"; Details = Map.ofList ["error", box err.Message] |})

    | Ok index ->
        if not quiet then printfn "\nRecommendation engine ready\n"

        // Simulate API requests
        let apiRequests = [|
            ("P001", "Product page view")
            ("P006", "After adding to cart")
            ("P010", "Checkout upsell")
        |]

        if not quiet then printfn "=== Simulating API Requests ===\n"

        let apiResults = System.Collections.Generic.List<obj>()

        apiRequests
        |> Array.iter (fun (productId, context) ->
            let product = catalog |> Array.find (fun p -> p.Id = productId)

            if not quiet then
                printfn "[API] GET /recommendations?product=%s&context=%s" productId context

            match SimilaritySearch.findSimilar product (extractFeatures product) 3 index with
            | Ok searchResults ->
                if not quiet then
                    let response =
                        searchResults.Matches
                        |> Array.map (fun m ->
                            sprintf """{ "id": "%s", "name": "%s", "similarity": %.2f }"""
                                m.Item.Id m.Item.Name m.Similarity
                        )
                        |> String.concat ", "
                    printfn "Response (200 OK): [%s]\n" response

                apiResults.Add(box {|
                    ProductId = productId
                    Context = context
                    Recommendations = searchResults.Matches |> Array.map (fun m ->
                        {| Id = m.Item.Id; Name = m.Item.Name; Similarity = m.Similarity |})
                |})

            | Error err ->
                if not quiet then printfn "Response (500 Error): %s\n" err.Message
                apiResults.Add(box {| ProductId = productId; Context = context; Error = err.Message |})
        )

        results.Add({| Example = "5-production"; Status = "ok"; Details = Map.ofList [
            "api_requests", box apiResults.Count
            "responses", box (apiResults.ToArray())
        ] |})

        // Integration patterns (verbose only)
        if not quiet then
            printfn "\n=== Integration Patterns ===\n"

            printfn "ASP.NET Core API:"
            printfn """
[ApiController]
[Route("api/recommendations")]
public class RecommendationsController : ControllerBase
{
    private readonly ISimilaritySearchIndex<Product> _index;
    
    [HttpGet("{productId}")]
    public IActionResult GetRecommendations(string productId, int count = 5)
    {
        var product = _catalog.Find(productId);
        var features = ExtractFeatures(product);
        var results = _index.FindSimilar(product, features, count);
        return Ok(results.Matches.Select(m => new {
            m.Item.Id, m.Item.Name, m.Item.Price,
            Similarity = m.Similarity
        }));
    }
}
"""

            printfn "Batch duplicate detection (nightly job):"
            printfn """
let detectDuplicates (catalog: Product[]) =
    let index = similaritySearch {
        indexItems (catalog |> Array.map (fun p -> (p, extractFeatures p)))
        threshold 0.9
        backend quantumBackend
    }
    match index with
    | Ok idx -> SimilaritySearch.findDuplicates 0.9 idx
    | Error e -> Error e
"""

// ==============================================================================
// OUTPUT
// ==============================================================================

let resultArray = results.ToArray()

if outputPath.IsSome then
    let payload = {| script = "ProductRecommendations.fsx"; timestamp = DateTime.UtcNow; results = resultArray |}
    Reporting.writeJson outputPath.Value payload
    if not quiet then printfn "\nResults written to %s" outputPath.Value

if csvPath.IsSome then
    let header = ["example"; "status"; "detail"]
    let rows =
        resultArray
        |> Array.map (fun r ->
            let detail =
                r.Details
                |> Map.toList
                |> List.map (fun (k, v) -> sprintf "%s=%O" k v)
                |> String.concat "; "
            [r.Example; r.Status; detail])
        |> Array.toList
    Reporting.writeCsv csvPath.Value header rows
    if not quiet then printfn "CSV written to %s" csvPath.Value

if not quiet && argv.Length = 0 then
    printfn "\nExample complete!"
    printfn "\nTry these options:"
    printfn "  dotnet fsi ProductRecommendations.fsx -- --help"
    printfn "  dotnet fsi ProductRecommendations.fsx -- --example 3 --threshold 0.9"
    printfn "  dotnet fsi ProductRecommendations.fsx -- --quiet --output results.json"
    printfn "  dotnet fsi ProductRecommendations.fsx -- --example all --csv results.csv"
