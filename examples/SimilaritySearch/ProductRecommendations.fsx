// ==============================================================================
// Product Recommendations using Quantum Similarity Search
// ==============================================================================
// Compares quantum kernel-based similarity across a product catalog.  For each
// product, runs SimilaritySearch.findSimilar to identify the top matches,
// producing a ranked recommendation table.  Supports a custom catalog via CSV.
//
// Usage:
//   dotnet fsi ProductRecommendations.fsx
//   dotnet fsi ProductRecommendations.fsx -- --help
//   dotnet fsi ProductRecommendations.fsx -- --products P001,P006,P010
//   dotnet fsi ProductRecommendations.fsx -- --threshold 0.8 --top 3
//   dotnet fsi ProductRecommendations.fsx -- --input catalog.csv
//   dotnet fsi ProductRecommendations.fsx -- --quiet --output results.json --csv results.csv
//
// References:
//   [1] Havlicek et al., "Supervised learning with quantum-enhanced feature
//       spaces", Nature 567, 209-212 (2019).
//   [2] Wikipedia: Recommender_system
//       https://en.wikipedia.org/wiki/Recommender_system
// ==============================================================================

#r "../../src/FSharp.Azure.Quantum/bin/Debug/net10.0/FSharp.Azure.Quantum.dll"
#r "nuget: Microsoft.Extensions.Logging.Abstractions, 10.0.0"
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
// CLI
// ==============================================================================

let argv = fsi.CommandLineArgs |> Array.skip 1
let args = Cli.parse argv

Cli.exitIfHelp "ProductRecommendations.fsx"
    "Product recommendations using quantum similarity search."
    [ { Cli.OptionSpec.Name = "input"; Description = "CSV file with custom product catalog (id,name,price,category,rating,reviews)"; Default = Some "built-in catalog" }
      { Cli.OptionSpec.Name = "products"; Description = "Comma-separated product IDs to query (default: all)"; Default = Some "all" }
      { Cli.OptionSpec.Name = "threshold"; Description = "Similarity threshold (0.0-1.0)"; Default = Some "0.6" }
      { Cli.OptionSpec.Name = "top"; Description = "Number of recommendations per product"; Default = Some "3" }
      { Cli.OptionSpec.Name = "output"; Description = "Write results to JSON file"; Default = None }
      { Cli.OptionSpec.Name = "csv"; Description = "Write results to CSV file"; Default = None }
      { Cli.OptionSpec.Name = "quiet"; Description = "Suppress informational output (flag)"; Default = None } ]
    args

let quiet = Cli.hasFlag "quiet" args
let inputFile = Cli.tryGet "input" args
let productFilter = Cli.getCommaSeparated "products" args
let cliThreshold = Cli.getFloatOr "threshold" 0.6 args
let topN = Cli.getIntOr "top" 3 args

// ==============================================================================
// TYPES
// ==============================================================================

type Product =
    { Id: string
      Name: string
      Price: float
      Category: string
      Rating: float
      NumReviews: int }

type RecommendationResult =
    { QueryProduct: Product
      Matches: (Product * float * int) list  // (product, similarity, rank)
      HasQuantumFailure: bool }

// ==============================================================================
// FEATURE EXTRACTION
// ==============================================================================

let extractFeatures (product: Product) : float array =
    [| product.Price / 1000.0
       float product.NumReviews / 100.0
       product.Rating / 5.0
       if product.Category = "Electronics" then 1.0 else 0.0
       if product.Category = "Books" then 1.0 else 0.0
       if product.Category = "Clothing" then 1.0 else 0.0
       if product.Category = "Home" then 1.0 else 0.0 |]

// ==============================================================================
// BUILT-IN CATALOG
// ==============================================================================

let private builtinCatalog =
    [| { Id = "P001"; Name = "Laptop Pro 15\""; Price = 1299.99; Category = "Electronics"; Rating = 4.5; NumReviews = 234 }
       { Id = "P002"; Name = "Laptop Air 13\""; Price = 999.99; Category = "Electronics"; Rating = 4.7; NumReviews = 456 }
       { Id = "P003"; Name = "Tablet 10\""; Price = 499.99; Category = "Electronics"; Rating = 4.3; NumReviews = 189 }
       { Id = "P004"; Name = "Smartphone X"; Price = 899.99; Category = "Electronics"; Rating = 4.6; NumReviews = 678 }
       { Id = "P005"; Name = "Wireless Headphones"; Price = 249.99; Category = "Electronics"; Rating = 4.4; NumReviews = 345 }
       { Id = "P006"; Name = "Quantum Computing Explained"; Price = 39.99; Category = "Books"; Rating = 4.8; NumReviews = 89 }
       { Id = "P007"; Name = "Machine Learning Basics"; Price = 44.99; Category = "Books"; Rating = 4.7; NumReviews = 156 }
       { Id = "P008"; Name = "Python Programming"; Price = 34.99; Category = "Books"; Rating = 4.6; NumReviews = 234 }
       { Id = "P009"; Name = "Data Science Handbook"; Price = 49.99; Category = "Books"; Rating = 4.9; NumReviews = 123 }
       { Id = "P010"; Name = "Running Shoes"; Price = 89.99; Category = "Clothing"; Rating = 4.5; NumReviews = 456 }
       { Id = "P011"; Name = "Winter Jacket"; Price = 129.99; Category = "Clothing"; Rating = 4.6; NumReviews = 234 }
       { Id = "P012"; Name = "Cotton T-Shirt"; Price = 19.99; Category = "Clothing"; Rating = 4.3; NumReviews = 678 }
       { Id = "P013"; Name = "Jeans Classic"; Price = 59.99; Category = "Clothing"; Rating = 4.4; NumReviews = 345 }
       { Id = "P014"; Name = "Coffee Maker"; Price = 79.99; Category = "Home"; Rating = 4.5; NumReviews = 234 }
       { Id = "P015"; Name = "Blender Pro"; Price = 99.99; Category = "Home"; Rating = 4.7; NumReviews = 189 }
       { Id = "P016"; Name = "Vacuum Cleaner"; Price = 199.99; Category = "Home"; Rating = 4.6; NumReviews = 156 }
       { Id = "P017"; Name = "Air Purifier"; Price = 149.99; Category = "Home"; Rating = 4.8; NumReviews = 123 } |]

// ==============================================================================
// CSV LOADING
// ==============================================================================

let private loadCatalogFromCsv (path: string) : Product array =
    let rows, errors = Data.readCsvWithHeaderWithErrors path
    if not (List.isEmpty errors) && not quiet then
        for err in errors do eprintfn "  Warning (CSV): %s" err
    rows
    |> List.choose (fun row ->
        let get key = row.Values |> Map.tryFind key
        match get "id", get "name", get "category" with
        | Some id, Some name, Some cat ->
            let tryFloat key def = get key |> Option.bind (fun s -> match Double.TryParse s with true, v -> Some v | _ -> None) |> Option.defaultValue def
            let tryInt key def = get key |> Option.bind (fun s -> match Int32.TryParse s with true, v -> Some v | _ -> None) |> Option.defaultValue def
            Some { Id = id; Name = name; Category = cat
                   Price = tryFloat "price" 0.0
                   Rating = tryFloat "rating" 0.0
                   NumReviews = tryInt "reviews" 0 }
        | _ ->
            if not quiet then eprintfn "  Warning: row missing 'id', 'name', or 'category'"
            None)
    |> Array.ofList

// ==============================================================================
// CATALOG SELECTION
// ==============================================================================

let catalog : Product array =
    match inputFile with
    | Some path ->
        let resolved = Data.resolveRelative __SOURCE_DIRECTORY__ path
        if not quiet then printfn "Loading catalog from: %s" resolved
        loadCatalogFromCsv resolved
    | None -> builtinCatalog

if Array.isEmpty catalog then
    eprintfn "Error: No products loaded."
    exit 1

let queryProducts : Product array =
    match productFilter with
    | [] -> catalog
    | filters ->
        let filterSet = filters |> List.map (fun s -> s.ToUpperInvariant()) |> Set.ofList
        catalog
        |> Array.filter (fun p ->
            let key = p.Id.ToUpperInvariant()
            filterSet |> Set.exists (fun f -> key.Contains f))

if Array.isEmpty queryProducts then
    eprintfn "Error: No products matched filter. Available IDs: %s"
        (catalog |> Array.map (fun p -> p.Id) |> String.concat ", ")
    exit 1

// ==============================================================================
// QUANTUM BACKEND (Rule 1)
// ==============================================================================

let quantumBackend : IQuantumBackend = LocalBackend() :> IQuantumBackend

if not quiet then
    printfn ""
    printfn "=================================================================="
    printfn "  Product Recommendations (Quantum Similarity Search)"
    printfn "=================================================================="
    printfn ""
    printfn "  Backend:    %s" quantumBackend.Name
    printfn "  Catalog:    %d products" catalog.Length
    printfn "  Querying:   %d products" queryProducts.Length
    printfn "  Threshold:  %.2f" cliThreshold
    printfn "  Top N:      %d" topN
    printfn ""

// ==============================================================================
// BUILD INDEX
// ==============================================================================

let indexResult = similaritySearch {
    indexItems (catalog |> Array.map (fun p -> (p, extractFeatures p)))
    similarityMetric Cosine
    threshold cliThreshold
    backend quantumBackend
}

match indexResult with
| Error err ->
    eprintfn "Error: Index build failed: %s" err.Message
    exit 1
| Ok _ ->
    if not quiet then printfn "  Index built: %d items, threshold=%.2f" catalog.Length cliThreshold

let index =
    match indexResult with
    | Ok idx -> idx
    | Error _ -> failwith "unreachable"

// ==============================================================================
// QUERY EACH PRODUCT
// ==============================================================================

let queryProduct (product: Product) : RecommendationResult =
    match SimilaritySearch.findSimilar product (extractFeatures product) topN index with
    | Ok searchResults ->
        let matches =
            searchResults.Matches
            |> Array.toList
            |> List.map (fun m -> (m.Item, m.Similarity, m.Rank))
        if not quiet then
            printfn "  %s (%s, $%.2f) -> %d matches"
                product.Name product.Category product.Price matches.Length
        { QueryProduct = product; Matches = matches; HasQuantumFailure = false }
    | Error err ->
        if not quiet then eprintfn "  %s: search failed: %s" product.Id err.Message
        { QueryProduct = product; Matches = []; HasQuantumFailure = true }

let results = queryProducts |> Array.toList |> List.map queryProduct

// ==============================================================================
// RANKED COMPARISON TABLE
// ==============================================================================

let printTable () =
    printfn ""
    printfn "=================================================================="
    printfn "  Product Recommendations (Quantum Similarity)"
    printfn "=================================================================="
    printfn ""
    printfn "  %-6s  %-28s  %-10s  %-28s  %8s  %6s"
        "Query" "Product" "Category" "Recommendation" "Price" "Match%%"
    printfn "  %s" (String('=', 100))

    results
    |> List.iter (fun r ->
        match r.Matches with
        | [] ->
            printfn "  %-6s  %-28s  %-10s  %-28s  %8s  %6s"
                r.QueryProduct.Id r.QueryProduct.Name r.QueryProduct.Category
                "(no matches)" "" ""
        | matches ->
            matches
            |> List.iteri (fun i (m, sim, _) ->
                if i = 0 then
                    printfn "  %-6s  %-28s  %-10s  %-28s  %8.2f  %5.0f%%"
                        r.QueryProduct.Id r.QueryProduct.Name r.QueryProduct.Category
                        m.Name m.Price (sim * 100.0)
                else
                    printfn "  %-6s  %-28s  %-10s  %-28s  %8.2f  %5.0f%%"
                        "" "" "" m.Name m.Price (sim * 100.0)))

    printfn ""

printTable ()

// ==============================================================================
// SUMMARY
// ==============================================================================

if not quiet then
    let withMatches = results |> List.filter (fun r -> not (List.isEmpty r.Matches))
    let failed = results |> List.filter (fun r -> r.HasQuantumFailure)
    printfn "  Products queried:  %d" results.Length
    printfn "  With matches:      %d" withMatches.Length
    printfn "  Quantum failures:  %d" failed.Length
    printfn ""

// ==============================================================================
// STRUCTURED OUTPUT
// ==============================================================================

let resultMaps =
    results
    |> List.collect (fun r ->
        match r.Matches with
        | [] ->
            [ [ "query_id", r.QueryProduct.Id
                "query_name", r.QueryProduct.Name
                "query_category", r.QueryProduct.Category
                "query_price", sprintf "%.2f" r.QueryProduct.Price
                "match_rank", "1"
                "match_id", ""
                "match_name", ""
                "match_price", ""
                "similarity", ""
                "has_quantum_failure", string r.HasQuantumFailure ]
              |> Map.ofList ]
        | matches ->
            matches
            |> List.map (fun (m, sim, rank) ->
                [ "query_id", r.QueryProduct.Id
                  "query_name", r.QueryProduct.Name
                  "query_category", r.QueryProduct.Category
                  "query_price", sprintf "%.2f" r.QueryProduct.Price
                  "match_rank", string rank
                  "match_id", m.Id
                  "match_name", m.Name
                  "match_price", sprintf "%.2f" m.Price
                  "similarity", sprintf "%.4f" sim
                  "has_quantum_failure", string r.HasQuantumFailure ]
                |> Map.ofList))

match Cli.tryGet "output" args with
| Some path ->
    Reporting.writeJson path resultMaps
    if not quiet then printfn "Results written to %s" path
| None -> ()

match Cli.tryGet "csv" args with
| Some path ->
    let header =
        [ "query_id"; "query_name"; "query_category"; "query_price"
          "match_rank"; "match_id"; "match_name"; "match_price"
          "similarity"; "has_quantum_failure" ]
    let rows =
        resultMaps
        |> List.map (fun m ->
            header |> List.map (fun h -> m |> Map.tryFind h |> Option.defaultValue ""))
    Reporting.writeCsv path header rows
    if not quiet then printfn "Results written to %s" path
| None -> ()

if argv.Length = 0 && not quiet then
    printfn ""
    printfn "Tip: Run with --help to see all options."
    printfn "     --products P001,P006   Query specific products"
    printfn "     --threshold 0.8        Raise similarity threshold"
    printfn "     --input catalog.csv    Load custom product catalog"
    printfn "     --csv results.csv      Export recommendations as CSV"
    printfn ""
