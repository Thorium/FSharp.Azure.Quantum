/// Similarity Search Example: Product Recommendations
/// 
/// This example demonstrates how to use the SimilaritySearchBuilder
/// to build a product recommendation system.
///
/// BUSINESS PROBLEM:
/// - Show customers "similar products" they might be interested in
/// - Find duplicate products in catalog (data quality)
/// - Group products into categories automatically
/// 
/// APPROACH:
/// Index products with their features (price, category, ratings, etc.)
/// Use quantum kernel similarity to find most similar items.

//#r "nuget: FSharp.Azure.Quantum"
#r "../../src/FSharp.Azure.Quantum/bin/Debug/net10.0/FSharp.Azure.Quantum.dll"

open System
open FSharp.Azure.Quantum.Business
open FSharp.Azure.Quantum.Business.SimilaritySearch

// ============================================================================
// SAMPLE DATA - Product Catalog
// ============================================================================

/// Product type for our e-commerce site
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
        // Category encoding (one-hot would be better for production)
        if product.Category = "Electronics" then 1.0 else 0.0
        if product.Category = "Books" then 1.0 else 0.0
        if product.Category = "Clothing" then 1.0 else 0.0
        if product.Category = "Home" then 1.0 else 0.0
    |]

/// Generate sample product catalog
let generateCatalog () =
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

// ============================================================================
// EXAMPLE 1: Basic Product Recommendations
// ============================================================================

printfn "=== Example 1: Product Recommendations (Basic) ===\n"

let catalog = generateCatalog()

printfn "Indexing %d products...\n" catalog.Length

// Index products with their features
let indexResult = similaritySearch {
    indexItems (catalog |> Array.map (fun p -> (p, extractFeatures p)))
    similarityMetric Cosine
    threshold 0.6
}

match indexResult with
| Error err ->
    printfn "❌ Indexing failed: %s" err.Message

| Ok index ->
    printfn "✅ Index built successfully\n"
    
    // Customer is viewing a laptop
    let currentProduct = catalog.[0]  // Laptop Pro 15"
    
    printfn "Customer viewing: %s ($%.2f)" currentProduct.Name currentProduct.Price
    printfn "\nRecommendations:\n"
    
    match SimilaritySearch.findSimilar currentProduct (extractFeatures currentProduct) 5 index with
    | Ok results ->
        results.Matches
        |> Array.iter (fun m ->
            printfn "  %d. %s" m.Rank m.Item.Name
            printfn "     $%.2f | Rating: %.1f ⭐ | %.0f%% match"
                m.Item.Price m.Item.Rating (m.Similarity * 100.0)
            printfn ""
        )
        
        printfn "Search completed in: %A" results.SearchTime
    
    | Error err ->
        printfn "❌ Search failed: %s" err.Message

// ============================================================================
// EXAMPLE 2: Cross-Sell Recommendations
// ============================================================================

printfn "\n=== Example 2: Cross-Sell Opportunities ===\n"

match indexResult with
| Ok index ->
    
    // Customer bought headphones - what else might they want?
    let purchasedProduct = catalog |> Array.find (fun p -> p.Id = "P005")  // Wireless Headphones
    
    printfn "Customer purchased: %s\n" purchasedProduct.Name
    printfn "Cross-sell suggestions:\n"
    
    match SimilaritySearch.findSimilar purchasedProduct (extractFeatures purchasedProduct) 3 index with
    | Ok results ->
        results.Matches
        |> Array.iter (fun m ->
            printfn "  ✓ %s (%.0f%% match)" m.Item.Name (m.Similarity * 100.0)
            printfn "    \"Customers who bought %s also bought %s\""
                purchasedProduct.Name m.Item.Name
            printfn ""
        )
    
    | Error err ->
        printfn "❌ Search failed: %s" err.Message

| Error _ -> ()

// ============================================================================
// EXAMPLE 3: Duplicate Detection (Data Quality)
// ============================================================================

printfn "\n=== Example 3: Find Duplicate Products ===\n"

// Add some near-duplicate products
let catalogWithDuplicates =
    Array.append catalog [|
        { Id = "P018"; Name = "Laptop Pro 15\" (Renewed)"; Price = 1099.99; Category = "Electronics"; Rating = 4.4; NumReviews = 89 }
        { Id = "P019"; Name = "Laptop Pro 15\" - Refurbished"; Price = 1199.99; Category = "Electronics"; Rating = 4.3; NumReviews = 56 }
    |]

let dupIndexResult = similaritySearch {
    indexItems (catalogWithDuplicates |> Array.map (fun p -> (p, extractFeatures p)))
    similarityMetric Cosine
}

match dupIndexResult with
| Ok index ->
    printfn "Scanning %d products for duplicates...\n" catalogWithDuplicates.Length
    
    match SimilaritySearch.findDuplicates 0.85 index with
    | Ok groups ->
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
    
    | Error err ->
        printfn "❌ Duplicate detection failed: %s" err.Message

| Error err ->
    printfn "❌ Indexing failed: %s" err.Message

// ============================================================================
// EXAMPLE 4: Automatic Product Clustering
// ============================================================================

printfn "\n=== Example 4: Automatic Product Clustering ===\n"

match indexResult with
| Ok index ->
    printfn "Clustering products into 4 groups...\n"
    
    match SimilaritySearch.cluster 4 100 index with
    | Ok clusters ->
        clusters
        |> Array.iteri (fun i cluster ->
            printfn "Cluster %d (%d products):" (i+1) cluster.Length
            cluster
            |> Array.take (min 5 cluster.Length)
            |> Array.iter (fun p ->
                printfn "  • %s ($%.2f, %s)" p.Name p.Price p.Category
            )
            if cluster.Length > 5 then
                printfn "  ... and %d more" (cluster.Length - 5)
            printfn ""
        )
    
    | Error err ->
        printfn "❌ Clustering failed: %s" err.Message

| Error _ -> ()

// ============================================================================
// EXAMPLE 5: Production Recommendation API
// ============================================================================

printfn "\n=== Example 5: Production Recommendation Engine ===\n"

match similaritySearch {
    indexItems (catalog |> Array.map (fun p -> (p, extractFeatures p)))
    similarityMetric Cosine
    threshold 0.6
    verbose true
} with

| Ok index ->
    printfn "\n✅ Recommendation engine ready\n"
    
    // Simulate API requests
    let apiRequests = [|
        ("P001", "Product page view")
        ("P006", "After adding to cart")
        ("P010", "Checkout upsell")
    |]
    
    printfn "=== Simulating API Requests ===\n"
    
    apiRequests
    |> Array.iter (fun (productId, context) ->
        let product = catalog |> Array.find (fun p -> p.Id = productId)
        
        printfn "[API] GET /recommendations?product=%s&context=%s" productId context
        
        match SimilaritySearch.findSimilar product (extractFeatures product) 3 index with
        | Ok results ->
            let response = 
                results.Matches
                |> Array.map (fun m ->
                    sprintf """{ "id": "%s", "name": "%s", "similarity": %.2f }""" 
                        m.Item.Id m.Item.Name m.Similarity
                )
                |> String.concat ", "
            
            printfn "Response (200 OK): [%s]" response
            printfn ""
        
        | Error err ->
            printfn "Response (500 Error): %s\n" err.Message
    )

| Error err ->
    printfn "❌ Engine initialization failed: %s" err.Message

// ============================================================================
// INTEGRATION PATTERNS
// ============================================================================

printfn "\n=== Integration Patterns ===\n"

printfn "ASP.NET Core API:"
printfn """
[ApiController]
[Route("api/recommendations")]
public class RecommendationsController : ControllerBase
{
    private readonly ISimilaritySearchIndex<Product> _index;
    
    public RecommendationsController(ISimilaritySearchIndex<Product> index)
    {
        _index = index;
    }
    
    [HttpGet("{productId}")]
    public IActionResult GetRecommendations(string productId, [FromQuery] int count = 5)
    {
        var product = _catalog.Find(productId);
        var features = ExtractFeatures(product);
        
        var results = _index.FindSimilar(product, features, count);
        
        return Ok(results.Matches.Select(m => new {
            m.Item.Id,
            m.Item.Name,
            m.Item.Price,
            Similarity = m.Similarity,
            Reason = $"{(m.Similarity * 100):F0}%% match"
        }));
    }
    
    [HttpPost("batch")]
    public IActionResult GetBatchRecommendations([FromBody] string[] productIds)
    {
        var recommendations = productIds
            .Select(id => {
                var product = _catalog.Find(id);
                var features = ExtractFeatures(product);
                return new {
                    ProductId = id,
                    Recommendations = _index.FindSimilar(product, features, 3)
                };
            });
        
        return Ok(recommendations);
    }
}
"""

printfn "\nE-commerce Widget:"
printfn """
<!-- Product Page -->
<div class="similar-products">
    <h3>Customers Also Viewed</h3>
    @foreach (var match in recommendations.Take(4))
    {
        <div class="product-card">
            <img src="@match.Item.ImageUrl" />
            <h4>@match.Item.Name</h4>
            <p class="price">$@match.Item.Price</p>
            <span class="match">@((match.Similarity * 100).ToString("F0"))%% match</span>
        </div>
    }
</div>
"""

printfn "\nReal-time Personalization:"
printfn """
// Update recommendations as customer browses
$(document).on('product:viewed', function(e, productId) {
    $.get('/api/recommendations/' + productId + '?count=8', function(data) {
        updateRecommendationWidget(data);
        trackEvent('recommendations_shown', { product: productId });
    });
});

// Cross-sell in cart
$(document).on('cart:updated', function(e, cart) {
    var productIds = cart.items.map(i => i.productId);
    $.post('/api/recommendations/batch', productIds, function(data) {
        updateCrossSellWidget(data);
    });
});
"""

printfn "\nBatch Processing:"
printfn """
// Nightly job: Find all duplicates for data quality
[Function("DetectDuplicates")]
public async Task DetectDuplicates([TimerTrigger("0 0 2 * * *")] TimerInfo timer)
{
    var catalog = await _database.GetAllProducts();
    
    var index = new SimilaritySearchBuilder<Product>()
        .IndexItems(catalog.ToArray(), ExtractFeatures)
        .WithThreshold(0.9)
        .Build();
    
    var duplicates = index.FindDuplicates(threshold: 0.9);
    
    foreach (var group in duplicates)
    {
        await _database.FlagDuplicateGroup(group);
        await _notifications.AlertDataQualityTeam(group);
    }
    
    _logger.LogInformation($"Found {duplicates.Length} duplicate groups");
}
"""

printfn "\n✅ Example complete! See code for integration patterns."
printfn "\nKey Takeaways:"
printfn "  • Index products once, search many times (fast!)"
printfn "  • Cosine similarity works well for mixed features"
printfn "  • Quantum kernel for maximum accuracy (slower)"
printfn "  • Duplicate detection improves data quality"
printfn "  • Clustering reveals natural product groups"
