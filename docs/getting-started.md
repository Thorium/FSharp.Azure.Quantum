---
layout: default
title: Getting Started
---

# Getting Started with FSharp.Azure.Quantum

Welcome to **FSharp.Azure.Quantum** - an F# library for quantum-inspired optimization using Azure Quantum services. This guide will help you get up and running in 5 minutes.

## Installation

**NuGet Package:** [https://www.nuget.org/packages/FSharp.Azure.Quantum](https://www.nuget.org/packages/FSharp.Azure.Quantum)

### Via NuGet Package Manager

```bash
dotnet add package FSharp.Azure.Quantum --version 0.1.0-alpha
```

### Via Package Manager Console

```powershell
Install-Package FSharp.Azure.Quantum -Version 0.1.0-alpha
```

### Via .fsproj File

```xml
<ItemGroup>
  <PackageReference Include="FSharp.Azure.Quantum" Version="0.1.0-alpha" />
</ItemGroup>
```

## Prerequisites

- **.NET 10.0 or later**
- **F# 10.0 or later**
- **Azure Account** (for quantum backend access)
- **Azure Quantum Workspace** (optional, for quantum execution)

## Quick Start: Your First Optimization

Let's solve a simple Traveling Salesman Problem (TSP) using the HybridSolver:

```fsharp
open FSharp.Azure.Quantum.Classical

// Define a 5-city distance matrix
let distances = array2D [
    [0.0; 2.0; 9.0; 10.0; 7.0]
    [1.0; 0.0; 6.0;  4.0; 3.0]
    [15.0; 7.0; 0.0;  8.0; 3.0]
    [6.0; 3.0; 12.0;  0.0; 11.0]
    [10.0; 4.0; 8.0;  5.0; 0.0]
]

// Solve using HybridSolver (automatically chooses classical or quantum)
match HybridSolver.solveTsp distances None None None with
| Ok solution ->
    printfn "Method used: %A" solution.Method
    printfn "Best tour: %A" solution.Result.Tour
    printfn "Tour length: %.2f" solution.Result.TourLength
    printfn "Reasoning: %s" solution.Reasoning
    printfn "Time: %.2f ms" solution.ElapsedMs
| Error msg ->
    printfn "Error: %s" msg
```

**Output:**
```
Method used: Classical
Best tour: [|0; 1; 3; 2; 4|]
Tour length: 25.00
Reasoning: Problem size (5 cities) is small enough for classical optimization...
Time: 15.32 ms
```

## Key Concepts

### 1. **HybridSolver** - Automatic Quantum/Classical Routing

The `HybridSolver` automatically decides whether to use quantum or classical approaches based on problem size, complexity, and cost:

```fsharp
// Let the solver decide (recommended)
HybridSolver.solveTsp distances None None None

// Force classical solver
HybridSolver.solveTsp distances None None (Some HybridSolver.Classical)

// Set budget limit (when quantum available)
HybridSolver.solveTsp distances (Some 10.0) None None  // $10 USD limit
```

#### How HybridSolver Decides: Decision Flow

```
┌─────────────────────────────────────┐
│    HybridSolver.solveTsp()          │
└──────────────┬──────────────────────┘
               │
               ▼
       ┌───────────────┐
       │ forceMethod?  │
       └───┬───────┬───┘
           │       │
      Yes  │       │ None (auto)
           │       │
           ▼       ▼
    ┌──────────┐  ┌─────────────────┐
    │ Classical│  │ QuantumAdvisor  │
    │    or    │  │  analyzes:      │
    │ Quantum  │  │  - Problem size │
    │ (forced) │  │  - Complexity   │
    └──────────┘  │  - Structure    │
                  └────────┬─────────┘
                           │
                  ┌────────┴────────┐
                  ▼                 ▼
           Size < 20         20 ≤ Size ≤ 200
              │                     │
              ▼                     ▼
        ┌──────────┐         ┌──────────┐
        │Classical │         │ Quantum  │
        │(too small│         │(if budget│
        │for quantum)        │allows)   │
        └──────────┘         └──────────┘
                                  │
                         No quantum yet?
                                  │
                                  ▼
                           ┌──────────┐
                           │Classical │
                           │(fallback)│
                           └──────────┘
```

**Key Decision Factors:**
- **Size < 20**: Always Classical (too small for quantum advantage)
- **20 ≤ Size ≤ 200**: Considers Quantum (checks budget, structure)
- **Size > 200**: Recommends Quantum (if available) or Classical fallback
- **Budget = 0**: Forces Classical regardless of size
- **forceMethod**: Overrides all automatic decisions

### 2. **Classical Solvers** - Immediate Results

Use classical solvers directly for guaranteed fast results:

```fsharp
// TSP with classical solver
let tspSolution = TspSolver.solveWithDistances distances TspSolver.defaultConfig

// Portfolio optimization
let portfolio = PortfolioSolver.solveGreedyByRatio assets constraints PortfolioSolver.defaultConfig
```

### 3. **Builder Pattern** - Simple Domain API

Use builder APIs for named cities and assets:

```fsharp
// TSP Builder - with named cities and coordinates
let cities = [
    ("Seattle", 0.0, 0.0)
    ("Portland", 0.0, 174.0)
    ("San Francisco", 635.0, 807.0)
]

let tspProblem = TSP.createProblem cities
match TSP.solve tspProblem None with
| Ok tour -> printfn "Tour: %A, Distance: %.2f" tour.Cities tour.TotalDistance
| Error msg -> printfn "Error: %s" msg

// Portfolio Builder - with asset details
let assets = [
    ("AAPL", 0.12, 0.18, 150.0)  // symbol, return, risk, price
    ("MSFT", 0.10, 0.15, 300.0)
    ("GOOGL", 0.15, 0.20, 2800.0)
]

let portfolioProblem = Portfolio.createProblem assets 10000.0
match Portfolio.solve portfolioProblem None with
| Ok allocation -> printfn "Total: $%.2f, Return: %.2f%%" allocation.TotalValue (allocation.ExpectedReturn * 100.0)
| Error msg -> printfn "Error: %s" msg
```

## Common Pitfalls & How to Avoid Them

### ❌ Pitfall 1: Non-Square Distance Matrix

```fsharp
// ❌ WRONG: 3 cities but 2x3 matrix
let wrong = array2D [
    [0.0; 1.0; 2.0]
    [1.0; 0.0; 3.0]
]
// Error: "Distance matrix must be square (NxN)"
```

**✅ Fix:** Ensure rows = columns = number of cities
```fsharp
// ✅ CORRECT: 3 cities, 3x3 matrix
let correct = array2D [
    [0.0; 1.0; 2.0]
    [1.0; 0.0; 3.0]
    [2.0; 3.0; 0.0]
]
```

### ❌ Pitfall 2: Asymmetric Distance Matrix

```fsharp
// ❌ WRONG: Distance from A→B ≠ B→A
let asymmetric = array2D [
    [0.0; 10.0]
    [5.0; 0.0]   // 10 ≠ 5
]
// Warning: TSP assumes symmetric distances
```

**✅ Fix:** Make matrix symmetric
```fsharp
// ✅ CORRECT: Distance A↔B is same both ways
let symmetric = array2D [
    [0.0; 10.0]
    [10.0; 0.0]
]
```

### ❌ Pitfall 3: Ignoring Result Type

```fsharp
// ❌ WRONG: Not handling errors
let solution = HybridSolver.solveTsp distances None None None
printfn "%A" solution.Result  // Compiler error! 'solution' is Result<T,E>
```

**✅ Fix:** Always pattern match on Result
```fsharp
// ✅ CORRECT: Proper error handling
match HybridSolver.solveTsp distances None None None with
| Ok solution -> 
    printfn "Success: %A" solution.Result
| Error msg -> 
    eprintfn "Failed: %s" msg
```

### ❌ Pitfall 4: Budget Constraints Too Tight

```fsharp
// ❌ WRONG: Budget smaller than minimum asset price
let assets = [("AAPL", 0.12, 0.18, 150.0)]
let constraints: PortfolioSolver.Constraints = { Budget = 100.0; MinHolding = 0.0; MaxHolding = 1000.0 }
// Error: "Budget (100) is insufficient to purchase any asset (minimum price: 150)"
```

**✅ Fix:** Ensure budget ≥ cheapest asset price
```fsharp
// ✅ CORRECT: Budget can buy at least one share
let constraints: PortfolioSolver.Constraints = { Budget = 500.0; MinHolding = 0.0; MaxHolding = 500.0 }
```

### ❌ Pitfall 5: Type Inference Confusion

```fsharp
// ❌ WRONG: F# can't infer tuple structure
let cities = [("Seattle", 0.0, 0.0); ("Portland", 0.0, 174.0)]
let problem = TSP.createProblem cities  // Type error!
```

**✅ Fix:** Add type annotation
```fsharp
// ✅ CORRECT: Explicit type annotation
let cities: (string * float * float) list = [
    ("Seattle", 0.0, 0.0)
    ("Portland", 0.0, 174.0)
]
```

## Complete Error Handling Examples

### Robust TSP Solving with Recovery

```fsharp
open FSharp.Azure.Quantum.Classical

let solveTspRobust (distances: float[,]) =
    // Validate input
    let n = distances.GetLength(0)
    if n <> distances.GetLength(1) then
        Error "Distance matrix must be square"
    elif n < 2 then
        Error "Need at least 2 cities"
    else
        // Try solving with automatic routing
        match HybridSolver.solveTsp distances None None None with
        | Ok solution ->
            printfn "✓ Success using %A solver" solution.Method
            printfn "  Tour: %A" solution.Result.Tour
            printfn "  Length: %.2f" solution.Result.TourLength
            printfn "  Time: %.2f ms" solution.ElapsedMs
            Ok solution
            
        | Error msg ->
            // Log error and try classical fallback
            eprintfn "⚠ HybridSolver failed: %s" msg
            eprintfn "  Falling back to classical solver..."
            
            try
                let classicalSolution = 
                    TspSolver.solveWithDistances distances TspSolver.defaultConfig
                printfn "✓ Classical fallback succeeded"
                printfn "  Tour: %A" classicalSolution.Tour
                Ok classicalSolution
            with
            | ex ->
                eprintfn "✗ Classical fallback also failed: %s" ex.Message
                Error $"All solvers failed: {msg}; {ex.Message}"

// Usage
let distances = array2D [[0.0; 10.0]; [10.0; 0.0]]
match solveTspRobust distances with
| Ok _ -> printfn "Problem solved!"
| Error msg -> eprintfn "Could not solve: %s" msg
```

### Portfolio Optimization with Validation

```fsharp
let solvePortfolioSafely assets budget =
    // Validate assets
    let invalidAssets = 
        assets 
        |> List.filter (fun (symbol, ret, risk, price) -> 
            price <= 0.0 || risk < 0.0)
    
    if not (List.isEmpty invalidAssets) then
        Error $"Invalid assets: {invalidAssets}"
    elif budget <= 0.0 then
        Error $"Budget must be positive: {budget}"
    else
        let constraints: PortfolioSolver.Constraints = {
            Budget = budget
            MinHolding = 0.0
            MaxHolding = budget * 0.5  // Max 50% in any asset
        }
        
        // Create asset records
        let assetRecords = 
            assets 
            |> List.map (fun (symbol, ret, risk, price) -> {
                Symbol = symbol
                ExpectedReturn = ret
                Risk = risk
                Price = price
            })
        
        // Validate budget constraint
        match PortfolioSolver.validateBudgetConstraint assetRecords constraints with
        | validation when not validation.IsValid ->
            Error $"Validation failed: {String.concat "; " validation.Messages}"
        | _ ->
            // Solve
            match HybridSolver.solvePortfolio assetRecords constraints None None None with
            | Ok solution ->
                printfn "✓ Portfolio optimized using %A" solution.Method
                printfn "  Total Value: $%.2f" solution.Result.TotalValue
                printfn "  Expected Return: %.2f%%" (solution.Result.ExpectedReturn * 100.0)
                printfn "  Risk: %.2f" solution.Result.Risk
                printfn "  Sharpe Ratio: %.2f" solution.Result.SharpeRatio
                Ok solution
            | Error msg ->
                Error $"Solver failed: {msg}"

// Usage with error recovery
let assets = [
    ("AAPL", 0.12, 0.18, 150.0)
    ("MSFT", 0.10, 0.15, 300.0)
]

match solvePortfolioSafely assets 10000.0 with
| Ok solution -> 
    // Process successful result
    solution.Result.Allocations 
    |> List.iter (fun a -> printfn "  %s: $%.2f" a.Asset.Symbol a.Value)
| Error msg -> 
    // Handle failure gracefully
    eprintfn "Portfolio optimization failed: %s" msg
    eprintfn "Try: Increase budget or reduce constraints"
```

### Handling Timeout and Budget Limits

```fsharp
let solveTspWithLimits distances maxBudget maxTime =
    printfn "Solving with budget=$%.2f, timeout=%.0fms" maxBudget maxTime
    
    match HybridSolver.solveTsp distances (Some maxBudget) (Some maxTime) None with
    | Ok solution when solution.ElapsedMs > maxTime ->
        // Exceeded timeout (classical fallback might have taken longer)
        printfn "⚠ Solution found but exceeded timeout (%.2f ms)" solution.ElapsedMs
        Ok solution
        
    | Ok solution when solution.Method = HybridSolver.Classical ->
        // Classical was used (possibly due to budget)
        printfn "✓ Classical solver used (budget=$%.2f saved)" maxBudget
        Ok solution
        
    | Ok solution ->
        // Quantum or classical succeeded within limits
        printfn "✓ Solution found within limits"
        Ok solution
        
    | Error msg when msg.Contains("budget") ->
        // Budget constraint violated
        eprintfn "✗ Insufficient budget: %s" msg
        eprintfn "  Try increasing budget or forcing classical solver"
        Error msg
        
    | Error msg when msg.Contains("timeout") ->
        // Timeout occurred
        eprintfn "✗ Solver timeout: %s" msg
        eprintfn "  Try increasing timeout or reducing problem size"
        Error msg
        
    | Error msg ->
        // Other error
        eprintfn "✗ Solver error: %s" msg
        Error msg

// Usage: Set limits
let result = solveTspWithLimits distances 5.0 1000.0  // $5 budget, 1 second
```

## Next Steps

- **[API Reference](api-reference.md)** - Complete API documentation
- **[TSP Example](examples/tsp-example.md)** - Detailed TSP solving walkthrough
- **[Portfolio Example](examples/portfolio-example.md)** - Portfolio optimization guide
- **[Hybrid Solver Guide](examples/hybrid-solver.md)** - Advanced hybrid solver usage
- **[Quantum vs Classical](guides/quantum-vs-classical.md)** - Decision framework
- **[FAQ](faq.md)** - Frequently asked questions

## Need Help?

- **Issues:** [GitHub Issues](https://github.com/thorium/FSharp.Azure.Quantum/issues)
- **Examples:** See [examples/](examples/) directory
- **API Docs:** See [api-reference.md](api-reference.md)

## Authentication (for Quantum Backend)

When using quantum backends, you'll need Azure credentials:

```fsharp
open FSharp.Azure.Quantum

// Configure authentication using createConfig helper
// Note: Classical solvers don't need Azure credentials
// Only needed when using actual quantum backends

open System.Net.Http

let httpClient = new HttpClient()
let config = Client.createConfig 
    "your-subscription-id" 
    "your-resource-group" 
    "your-workspace-name" 
    httpClient

let client = Client.QuantumClient(config)

// Authentication handled automatically via DefaultAzureCredential
// Supports: Azure CLI, Managed Identity, Environment Variables, etc.
```

**Note:** Classical solvers work without Azure credentials and are perfect for development and testing!

---

**Ready to optimize!** 🚀 Continue with the [TSP Example](examples/tsp-example.md) for a detailed walkthrough.
