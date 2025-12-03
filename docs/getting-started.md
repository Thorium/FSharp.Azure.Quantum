---
layout: default
title: Getting Started
---

# Getting Started with FSharp.Azure.Quantum

Welcome to **FSharp.Azure.Quantum** - an F# library for quantum optimization using quantum algorithms (QAOA, VQE, QFT) with automatic backend selection (local simulation or Azure Quantum cloud). This guide will help you get up and running in 5 minutes.

## Installation

**NuGet Package:** [https://www.nuget.org/packages/FSharp.Azure.Quantum](https://www.nuget.org/packages/FSharp.Azure.Quantum)

### Via NuGet Package Manager

```bash
dotnet add package FSharp.Azure.Quantum --version 1.1.0
```

### Via Package Manager Console

```powershell
Install-Package FSharp.Azure.Quantum -Version 1.1.0
```

### Via .fsproj File

```xml
<ItemGroup>
  <PackageReference Include="FSharp.Azure.Quantum" Version="1.1.0" />
</ItemGroup>
```

## Prerequisites

- **.NET 10.0 or later**
- **F# 10.0 or later**
- **Azure Account** (for quantum backend access)
- **Azure Quantum Workspace** (optional, for quantum execution)

## Quick Start: Your First Quantum Optimization

Let's solve a simple Graph Coloring Problem using the quantum-first API:

```fsharp
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.GraphColoring

// Define a graph coloring problem (register allocation)
let problem = graphColoring {
    node "R1" ["R2"; "R3"]
    node "R2" ["R1"; "R4"]
    node "R3" ["R1"; "R4"]
    node "R4" ["R2"; "R3"]
}

// Solve using QAOA quantum algorithm (LocalBackend simulation)
match GraphColoring.solve problem 4 None with  // None = LocalBackend (default)
| Ok solution ->
    printfn "Colors used: %d" solution.ColorsUsed
    solution.Assignments 
    |> Map.iter (fun node color -> printfn "%s → %s" node color)
| Error msg -> 
    printfn "Error: %s" msg
```

**Output:**
```
Colors used: 2
R1 → Color_0
R2 → Color_1
R3 → Color_1
R4 → Color_0
```

**What happens:**
1. Computation expression builds graph coloring problem
2. `GraphColoring.solve` encodes problem as QUBO
3. QAOA quantum algorithm builds optimization circuit
4. LocalBackend simulates quantum circuit (up to 20 qubits, free)
5. Returns color assignments with validation

## Key Concepts

### 1. **Direct Quantum API** - Recommended Approach

Use high-level builders to solve problems with quantum algorithms directly:

```fsharp
// MaxCut problem (graph partitioning)
let vertices = ["A"; "B"; "C"; "D"]
let edges = [
    ("A", "B", 1.0)
    ("B", "C", 2.0)
    ("C", "D", 1.0)
    ("D", "A", 1.0)
]

let maxCutProblem = MaxCut.createProblem vertices edges

// Solve with QAOA on LocalBackend (simulation)
match MaxCut.solve maxCutProblem None with
| Ok solution ->
    printfn "Cut Value: %.2f" solution.CutValue
    printfn "Partition S: %A" solution.PartitionS
    printfn "Partition T: %A" solution.PartitionT
| Error msg -> printfn "Error: %s" msg
```

### 2. **HybridSolver** - Optional Optimization for Variable-Sized Problems

The `HybridSolver` provides automatic classical fallback for very small problems where quantum circuit overhead isn't beneficial:

```fsharp
open FSharp.Azure.Quantum.Classical

// TSP with 5 cities
let distances = array2D [
    [0.0; 2.0; 9.0; 10.0; 7.0]
    [1.0; 0.0; 6.0;  4.0; 3.0]
    [15.0; 7.0; 0.0;  8.0; 3.0]
    [6.0; 3.0; 12.0;  0.0; 11.0]
    [10.0; 4.0; 8.0;  5.0; 0.0]
]

// HybridSolver automatically routes to classical (fast) or quantum (scalable)
match HybridSolver.solveTsp distances None None None with
| Ok solution ->
    printfn "Method: %A" solution.Method          // Classical or Quantum
    printfn "Tour: %A" solution.Result.Tour
    printfn "Length: %.2f" solution.Result.TourLength
    printfn "Reasoning: %s" solution.Reasoning    // Explains routing decision
| Error msg -> printfn "Error: %s" msg
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
    │ Classical│  │ Analyze Problem │
    │    or    │  │  - Size         │
    │ Quantum  │  │  - Complexity   │
    │ (forced) │  │  - Structure    │
    └──────────┘  └────────┬─────────┘
                           │
                  ┌────────┴────────┐
                  ▼                 ▼
           Size < 20         20 ≤ Size ≤ 200
              │                     │
              ▼                     ▼
        ┌──────────┐         ┌──────────┐
        │Classical │         │ Quantum  │
        │(too small│         │(QAOA on  │
        │overhead) │         │backend)  │
        └──────────┘         └──────────┘
```

**Key Decision Factors:**
- **Size < 20**: Classical (quantum circuit overhead not beneficial yet)
- **20 ≤ Size ≤ 200**: Quantum (QAOA on LocalBackend or cloud)
- **Size > 200**: Quantum recommended (potential for quantum advantage)
- **forceMethod**: Overrides automatic decision

### 3. **Computation Expression Pattern** - Type-Safe Problem Specification

Builders provide domain-specific computation expressions for intuitive, type-safe problem construction:

```fsharp
// Knapsack problem (resource allocation)
let items = [
    ("Laptop", 2.0, 1000.0)   // (id, weight, value)
    ("Tablet", 1.0, 500.0)
    ("Phone", 0.5, 300.0)
]
let knapsackProblem = Knapsack.createProblem items 3.0  // capacity = 3.0

// Solve with QAOA quantum algorithm
match Knapsack.solve knapsackProblem None with
| Ok solution ->
    printfn "Total Value: %.2f" solution.TotalValue
    printfn "Total Weight: %.2f" solution.TotalWeight
    solution.SelectedItems |> List.iter (fun item -> printfn "  - %s" item.Id)
| Error msg -> printfn "Error: %s" msg
```

**When to Use Direct Quantum API vs HybridSolver:**

| Scenario | Recommendation | Reason |
|----------|---------------|--------|
| Learning quantum algorithms | **Direct API** | Consistent quantum experience |
| Production with fixed problem size | **Direct API** | Simple, predictable behavior |
| Production with variable size | **HybridSolver** | Optimizes small problems automatically |
| Prototyping | **Direct API** | LocalBackend is fast enough (≤20 qubits) |
| Performance-critical variable sizing | **HybridSolver** | Classical fallback saves overhead |

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
// ❌ WRONG: Not handling errors (would cause compiler error)
// let solution = HybridSolver.solveTsp distances None None None
// printfn "%A" solution.Result  // Compiler error! 'solution' is Result<T,E>
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
let constraintsFixed: PortfolioSolver.Constraints = { Budget = 500.0; MinHolding = 0.0; MaxHolding = 500.0 }
```

### ❌ Pitfall 5: Type Inference Confusion

**❌ WRONG:** F# can't infer tuple structure
```fsharp
// This would cause a type error:
// let cities = [("Seattle", 0.0, 0.0); ("Portland", 0.0, 174.0)]
// let problem = TSP.createProblem cities
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
            // Log error and return error (no classical fallback in this example)
            eprintfn "⚠ HybridSolver failed: %s" msg
            Error $"Solver failed: {msg}"

// Usage
let distancesRobust = array2D [[0.0; 10.0]; [10.0; 0.0]]
match solveTspRobust distancesRobust with
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
        Error $"Invalid assets: %A{invalidAssets}"
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
                PortfolioSolver.Asset.Symbol = symbol
                ExpectedReturn = ret
                Risk = risk
                Price = price
            })
        
        // Validate budget constraint
        match PortfolioSolver.validateBudgetConstraint assetRecords constraints with
        | validation when not validation.IsValid ->
            Error (sprintf "Validation failed: %s" (String.concat "; " validation.Messages))
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
let assets: (string * float * float * float) list = [
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
        
    | Ok solution when solution.Method = Classical ->
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

## Quantum TSP with Parameter Optimization 

FSharp.Azure.Quantum provides **automatic QAOA parameter optimization** - a variational quantum-classical loop that finds optimal circuit parameters for your specific problem:

```fsharp
open FSharp.Azure.Quantum.Quantum.QuantumTspSolver
open FSharp.Azure.Quantum.Core.BackendAbstraction

// Create distance matrix for 3-city TSP
let distances = array2D [
    [ 0.0; 1.0; 2.0 ]
    [ 1.0; 0.0; 1.5 ]
    [ 2.0; 1.5; 0.0 ]
]

// Option 1: Use default configuration (optimization enabled)
let backend = createLocalBackend()
match solve backend distances defaultConfig with
| Ok solution ->
    printfn "Best tour: %A" solution.Tour
    printfn "Tour length: %.2f" solution.TourLength
    printfn "Optimized parameters (gamma, beta): %A" solution.OptimizedParameters
    printfn "Optimization converged: %b" solution.OptimizationConverged
    printfn "Iterations: %d" solution.OptimizationIterations
| Error msg -> printfn "Error: %s" msg

// Option 2: Custom configuration for fine-tuning
let customConfig = {
    OptimizationShots = 100       // Low shots for fast parameter search
    FinalShots = 1000             // High shots for accurate final result
    EnableOptimization = true     // Enable variational loop
    InitialParameters = (0.5, 0.5) // Starting guess for (gamma, beta)
}
let result = solve backend distances customConfig

// Option 3: Disable optimization (backward compatibility)
let resultNoOpt = solveWithShots backend distances 1000
```

### How QAOA Parameter Optimization Works

**Variational Quantum-Classical Loop:**
1. **Classical optimizer** proposes QAOA parameters (gamma, beta)
2. **Quantum backend** executes QAOA circuit with those parameters (low shots for speed)
3. **Measure tour quality** - Decode bitstrings to TSP tours and calculate cost
4. **Optimizer updates** parameters based on gradient-free Nelder-Mead simplex method
5. **Repeat until convergence** (~10-50 iterations typically)
6. **Final execution** uses optimized parameters with high shots for accurate result

**Benefits:**
- ✅ **Better solutions** - Problem-specific parameters → higher success probability
- ✅ **Research-grade** - Matches published QAOA implementations
- ✅ **Configurable** - Easy to adjust optimization/final shots for speed vs. accuracy
- ✅ **Backward compatible** - Old API still works via `solveWithShots`

**Configuration Guidelines:**
- `OptimizationShots = 100` - Fast parameter search (increase for noisy hardware)
- `FinalShots = 1000` - Accurate result (decrease for faster demos)
- `EnableOptimization = true` - Enable variational loop (disable for testing)
- `InitialParameters = (0.5, 0.5)` - Starting guess (γ, β ∈ [0, 2π])

**Performance:**
- **Extra cost:** ~20-50 optimization iterations × 100 shots = 2,000-5,000 shots
- **Time:** +10-30 seconds for optimization on local simulator
- **Quality improvement:** Problem-dependent, typically 5-20% better tour quality

For more details, see:
- **[Quantum TSP Example](examples/quantum-tsp-example.md)** - Complete QAOA walkthrough
- **[Local Simulation Guide](local-simulation.md)** - Quantum simulation without Azure

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

## Authentication (for Cloud Quantum Backends)

When using cloud quantum backends (IonQ, Rigetti via Azure Quantum), you'll need Azure credentials:

```fsharp
open FSharp.Azure.Quantum

// Configure authentication for Azure Quantum cloud
let workspace = {
    SubscriptionId = "your-subscription-id"
    ResourceGroup = "your-resource-group"
    WorkspaceName = "your-workspace-name"
    Location = "eastus"
}

// Authentication handled automatically via DefaultAzureCredential
// Supports: Azure CLI, Managed Identity, Environment Variables, etc.
```

**Note:** LocalBackend (default) works without Azure credentials - perfect for development, testing, and small problems (≤20 qubits)!

---

**Ready to optimize!** 🚀 Continue with the [TSP Example](examples/tsp-example.md) for a detailed walkthrough.
