# Getting Started with FSharp.Azure.Quantum

Welcome to **FSharp.Azure.Quantum** - an F# library for quantum-inspired optimization using Azure Quantum services. This guide will help you get up and running in 5 minutes.

## Installation

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
HybridSolver.solveTsp distances None None (Some HybridSolver.SolverMethod.Classical)

// Set budget limit (when quantum available)
HybridSolver.solveTsp distances (Some 10.0) None None  // $10 USD limit
```

### 2. **Classical Solvers** - Immediate Results

Use classical solvers directly for guaranteed fast results:

```fsharp
// TSP with classical solver
let tspSolution = TspSolver.solveWithDistances distances TspSolver.defaultConfig

// Portfolio optimization
let portfolio = PortfolioSolver.solveGreedyByRatio assets constraints PortfolioSolver.defaultConfig
```

### 3. **Builder Pattern** - Fluent API

Use builder APIs for complex problem construction:

```fsharp
// TSP Builder
let tspProblem = 
    Tsp.createProblem()
    |> Tsp.withCities ["Seattle"; "Portland"; "San Francisco"]
    |> Tsp.withDistanceMatrix distances

let solution = Tsp.solve tspProblem

// Portfolio Builder
let portfolioProblem =
    Portfolio.createProblem()
    |> Portfolio.withAssets assets
    |> Portfolio.withBudget 10000.0
    |> Portfolio.withRiskTolerance 0.3

let allocation = Portfolio.solve portfolioProblem
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

// Configure authentication
let workspace = {
    SubscriptionId = "your-subscription-id"
    ResourceGroup = "your-resource-group"
    WorkspaceName = "your-workspace-name"
    Location = "eastus"
}

// Authentication handled automatically via DefaultAzureCredential
// Supports: Azure CLI, Managed Identity, Environment Variables, etc.
```

**Note:** Classical solvers work without Azure credentials and are perfect for development and testing!

---

**Ready to optimize!** 🚀 Continue with the [TSP Example](examples/tsp-example.md) for a detailed walkthrough.
