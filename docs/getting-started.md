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
dotnet add package FSharp.Azure.Quantum
```

### Via Package Manager Console

```powershell
Install-Package FSharp.Azure.Quantum
```

### Via .fsproj File

```xml
<ItemGroup>
  <PackageReference Include="FSharp.Azure.Quantum" />
</ItemGroup>
```

> **Note:** Omitting the version will install the latest stable version. To specify a particular version, add `--version X.Y.Z` (CLI) or `Version="X.Y.Z"` (XML).

## Prerequisites

- **.NET 10.0 or later**
- **F# 10.0 or later**
- **Azure Account and Azure Quantum Workspace** (optional, only for cloud quantum backends; the local simulator needs neither)

## Quick Start: Your First Quantum Optimization

Let's solve a simple Graph Coloring Problem using the quantum-first API:

```fsharp
open System.Threading
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.GraphColoring

// Define a graph coloring problem (register allocation)
let problem = graphColoring {
    node "R1" ["R2"; "R3"]
    node "R2" ["R1"; "R4"]
    node "R3" ["R1"; "R4"]
    node "R4" ["R2"; "R3"]
    colors ["EAX"; "EBX"; "ECX"; "EDX"]   // required: the builder rejects a problem without colors
}

// Solve using QAOA quantum algorithm (LocalBackend simulation)
task {
    match! GraphColoring.solveAsync problem 4 None CancellationToken.None with  // None = LocalBackend (default)
    | Ok solution ->
        printfn "Colors used: %d" solution.ColorsUsed
        solution.Assignments 
        |> Map.iter (fun node color -> printfn "%s → %s" node color)
    | Error err -> 
        printfn "Error: %s" err.Message
}
```

Samples are `task` blocks: `await` them in an application, or end a script with `|> Async.AwaitTask |> Async.RunSynchronously`.

**Output** (QAOA is probabilistic, so the exact assignment can differ between runs):
```
Colors used: 2
R1 → EAX
R2 → EBX
R3 → EBX
R4 → EAX
```

**What happens:**
1. Computation expression builds graph coloring problem
2. `GraphColoring.solveAsync` encodes problem as QUBO
3. QAOA quantum algorithm builds optimization circuit
4. LocalBackend simulates quantum circuit (memory-derived width, free); this problem needs 4 nodes × 4 colors = 16 qubits
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
task {
    match! MaxCut.solveAsync maxCutProblem None CancellationToken.None with
    | Ok solution ->
        printfn "Cut Value: %.2f" solution.CutValue
        printfn "Partition S: %A" solution.PartitionS
        printfn "Partition T: %A" solution.PartitionT
    | Error err -> printfn "Error: %s" err.Message
}
```

### 2. **HybridSolver** - Optional Routing for Variable-Sized Problems

The `HybridSolver` solves small problems with classical heuristics and routes large ones to quantum when you supply a backend:

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

// 5 cities is below the advisor's thresholds, so this runs the classical solver
task {
    match! HybridSolver.solveTspAsync distances None None None CancellationToken.None with
    | Ok solution ->
        printfn "Method: %A" solution.Method          // Classical or Quantum
        printfn "Tour: %A" solution.Result.Tour
        printfn "Length: %.2f" solution.Result.TourLength
        printfn "Reasoning: %s" solution.Reasoning    // Explains routing decision
    | Error err -> printfn "Error: %s" err.Message
}
```

#### How HybridSolver Decides: Decision Flow

```
┌─────────────────────────────────────┐
│    HybridSolver.solveTspAsync()     │
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
    ┌──────────┐  ┌──────────────────┐
    │ Classical│  │ QuantumAdvisor   │
    │    or    │  │  - Size          │
    │ Quantum  │  │  - Estimated     │
    │ (forced) │  │    speedup       │
    └──────────┘  └────────┬─────────┘
                           │
                  ┌────────┴────────┐
                  ▼                 ▼
             Size < 50          Size ≥ 50
              │                     │
              ▼                     ▼
        ┌──────────┐     ┌─────────────────────┐
        │Classical │     │ Backend supplied and │
        │          │     │ cost within budget?  │
        └──────────┘     └──────┬─────────┬─────┘
                            Yes │         │ No
                                ▼         ▼
                         ┌──────────┐ ┌──────────┐
                         │ Quantum  │ │Classical │
                         │(QAOA on  │ │          │
                         │backend)  │ │          │
                         └──────────┘ └──────────┘
```

**Key Decision Factors** (with `QuantumAdvisor.defaultThresholds`):
- **Size < 20**: Classical, strongly recommended
- **20 ≤ Size < 50**: Advisor says "consider quantum", but HybridSolver still runs the classical solver
- **Size ≥ 50**: Quantum, if you called a `solve*WithBackendAsync` function with a backend and the estimated cost is within the optional budget; otherwise classical
- **forceMethod**: Overrides the decision (`Some HybridSolver.Quantum` uses the given backend, or a new LocalBackend)

`solution.Reasoning` always says which branch was taken.

### 3. **Problem Builders** - Type-Safe Problem Specification

Each problem type has a builder: a computation expression (`graphColoring { ... }`, `scheduledTask { ... }`) or `createProblem` helpers that take plain tuples:

```fsharp
open System.Threading
// Knapsack problem (resource allocation)
let items = [
    ("Laptop", 2.0, 1000.0)   // (id, weight, value)
    ("Tablet", 1.0, 500.0)
    ("Phone", 0.5, 300.0)
]
let knapsackProblem = Knapsack.createProblem items 3.0  // capacity = 3.0

// Solve with QAOA quantum algorithm
task {
    match! Knapsack.solveAsync knapsackProblem None CancellationToken.None with
    | Ok solution ->
        printfn "Total Value: %.2f" solution.TotalValue
        printfn "Total Weight: %.2f" solution.TotalWeight
        solution.SelectedItems |> List.iter (fun item -> printfn "  - %s" item.Id)
    | Error err -> printfn "Error: %s" err.Message
}
```

**When to Use Direct Quantum API vs HybridSolver:**

| Scenario | Recommendation | Reason |
|----------|---------------|--------|
| Learning quantum algorithms | **Direct API** | Every run goes through a quantum backend |
| Fixed problem size that fits the backend | **Direct API** | Simple, predictable behavior |
| Variable problem size | **HybridSolver** | Small problems are solved classically |
| Prototyping | **Direct API** | LocalBackend is fast enough at small widths |
| Problems too large for any backend | **HybridSolver** (classical path) | No qubit limit on the classical heuristics |

## Common Pitfalls & How to Avoid Them

### ❌ Pitfall 1: Non-Square Distance Matrix

```fsharp
// ❌ WRONG: 3 cities but 2x3 matrix
let wrong = array2D [
    [0.0; 1.0; 2.0]
    [1.0; 0.0; 3.0]
]
// HybridSolver.solveTspAsync returns
// Error (ValidationError "Distance matrix must be square (got 2x3 dimensions)")
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
// No error or warning: the TSP solvers assume symmetric distances,
// so tour lengths on this matrix are not meaningful
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
// let! solution = HybridSolver.solveTspAsync distances None None None CancellationToken.None
// printfn "%A" solution.Result  // Compiler error! 'solution' is Result<T,E>
```

**✅ Fix:** Always pattern match on Result
```fsharp
open System.Threading
// ✅ CORRECT: Proper error handling
task {
    match! HybridSolver.solveTspAsync distances None None None CancellationToken.None with
    | Ok solution -> 
        printfn "Success: %A" solution.Result
    | Error err -> 
        eprintfn "Failed: %s" err.Message
}
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

### ❌ Pitfall 5: Treating Errors as Strings

Solvers return `QuantumResult<'T>`, which is `Result<'T, QuantumError>`. The error is a union (`ValidationError`, `OperationError`, `BackendError`, ...), not a string.

```fsharp
// ❌ WRONG: 'err' is a QuantumError, so %s and err.Contains do not compile
// | Error err -> printfn "Error: %s" err
```

**✅ Fix:** Use `err.Message` for text, or match on the case
```fsharp
open FSharp.Azure.Quantum.Core

task {
    match! HybridSolver.solveTspAsync wrong None None None CancellationToken.None with
    | Ok _ -> ()
    | Error (QuantumError.ValidationError (field, reason)) -> eprintfn "Invalid %s: %s" field reason
    | Error err -> eprintfn "Failed (%s): %s" err.Category err.Message
}
```

## Complete Error Handling Examples

### Robust TSP Solving with Recovery

```fsharp
open FSharp.Azure.Quantum.Core

let solveTspRobust (distances: float[,]) (cancellationToken: CancellationToken) =
    task {
        // Validate input
        let n = distances.GetLength(0)
        if n <> distances.GetLength(1) then
            return Error (QuantumError.ValidationError ("distances", "Distance matrix must be square"))
        elif n < 2 then
            return Error (QuantumError.ValidationError ("distances", "Need at least 2 cities"))
        else
            // Try solving with automatic routing
            match! HybridSolver.solveTspAsync distances None None None cancellationToken with
            | Ok solution ->
                printfn "✓ Success using %A solver" solution.Method
                printfn "  Tour: %A" solution.Result.Tour
                printfn "  Length: %.2f" solution.Result.TourLength
                printfn "  Time: %.2f ms" solution.ElapsedMs
                return Ok solution
                
            | Error err ->
                // Log error and return error (no classical fallback in this example)
                eprintfn "⚠ HybridSolver failed: %s" err.Message
                return Error err
    }

// Usage
let distancesRobust = array2D [[0.0; 10.0]; [10.0; 0.0]]
task {
    match! solveTspRobust distancesRobust CancellationToken.None with
    | Ok _ -> printfn "Problem solved!"
    | Error err -> eprintfn "Could not solve: %s" err.Message
}
```

### Portfolio Optimization with Validation

```fsharp
open System.Threading
let solvePortfolioSafely
    (assets: (string * float * float * float) list)
    (budget: float)
    (cancellationToken: CancellationToken) =
    task {
        // Validate assets
        let invalidAssets = 
            assets 
            |> List.filter (fun (symbol, ret, risk, price) -> 
                price <= 0.0 || risk < 0.0)
        
        if not (List.isEmpty invalidAssets) then
            return Error (QuantumError.ValidationError ("assets", $"Invalid assets: %A{invalidAssets}"))
        elif budget <= 0.0 then
            return Error (QuantumError.ValidationError ("budget", $"Budget must be positive: {budget}"))
        else
            let constraints: PortfolioSolver.Constraints = {
                Budget = budget
                MinHolding = 0.0
                MaxHolding = budget * 0.5  // Max 50% in any asset
            }
            
            // Create asset records
            let assetRecords: PortfolioSolver.Asset list = 
                assets 
                |> List.map (fun (symbol, ret, risk, price) ->
                    { Symbol = symbol; ExpectedReturn = ret; Risk = risk; Price = price })
            
            // Validate budget constraint
            match PortfolioSolver.validateBudgetConstraint assetRecords constraints with
            | validation when not validation.IsValid ->
                return Error (QuantumError.ValidationError ("constraints", String.concat "; " validation.Messages))
            | _ ->
                // Solve
                match! HybridSolver.solvePortfolioAsync assetRecords constraints None None None cancellationToken with
                | Ok solution ->
                    printfn "✓ Portfolio optimized using %A" solution.Method
                    printfn "  Total Value: $%.2f" solution.Result.TotalValue
                    printfn "  Expected Return: %.2f%%" (solution.Result.ExpectedReturn * 100.0)
                    printfn "  Risk: %.2f" solution.Result.Risk
                    printfn "  Sharpe Ratio: %.2f" solution.Result.SharpeRatio
                    return Ok solution
                | Error err ->
                    return Error err
    }

// Usage with error recovery
let assets: (string * float * float * float) list = [
    ("AAPL", 0.12, 0.18, 150.0)
    ("MSFT", 0.10, 0.15, 300.0)
]

task {
    match! solvePortfolioSafely assets 10000.0 CancellationToken.None with
    | Ok solution -> 
        // Process successful result
        solution.Result.Allocations 
        |> List.iter (fun a -> printfn "  %s: $%.2f" a.Asset.Symbol a.Value)
    | Error err -> 
        // Handle failure gracefully
        eprintfn "Portfolio optimization failed: %s" err.Message
        eprintfn "Try: Increase budget or reduce constraints"
}
```

`HybridSolver.solvePortfolioAsync` treats the assets as independent (risk = sqrt(Σ (wᵢσᵢ)²)). When you have the covariance of the returns, `HybridSolver.solvePortfolioWithCovarianceAsync assets covariance constraints None None None None cancellationToken` validates it (square, one row per asset, symmetric, positive semidefinite, otherwise a `ValidationError`) and both solver paths report risk as sqrt(wᵀΣw).

### Handling Budget Limits

The `budget` argument (USD) is a cost guard: when the advisor recommends quantum but the estimated cost exceeds the budget, HybridSolver runs the classical solver instead and says so in `Reasoning`. Exceeding the budget is not an error.

```fsharp
let solveTspWithBudget (distances: float[,]) (maxBudget: float) (cancellationToken: CancellationToken) =
    task {
        printfn "Solving with budget=$%.2f" maxBudget
        
        match! HybridSolver.solveTspAsync distances (Some maxBudget) None None cancellationToken with
        | Ok solution when solution.Method = HybridSolver.Classical ->
            // Classical was used (small problem, no backend, or over budget)
            printfn "✓ Classical solver used: %s" solution.Reasoning
            return Ok solution
            
        | Ok solution ->
            printfn "✓ Quantum solver used: %s" solution.Reasoning
            return Ok solution
            
        | Error (QuantumError.ValidationError (field, reason)) ->
            eprintfn "✗ Invalid input (%s): %s" field reason
            return Error (QuantumError.ValidationError (field, reason))
            
        | Error err ->
            eprintfn "✗ Solver error: %s" err.Message
            return Error err
    }

// Usage: $5 budget
let result = solveTspWithBudget distances 5.0 CancellationToken.None
```

> The `timeout` argument of the HybridSolver functions is accepted but not currently used; there is no solver timeout.

## Quantum TSP with Parameter Optimization 

FSharp.Azure.Quantum provides **automatic QAOA parameter optimization** - a variational quantum-classical loop that finds optimal circuit parameters for your specific problem:

```fsharp
open System.Threading
open FSharp.Azure.Quantum.Quantum.QuantumTspSolver
open FSharp.Azure.Quantum.Backends

// Create distance matrix for 3-city TSP (3² = 9 qubits)
let distances = array2D [
    [ 0.0; 1.0; 2.0 ]
    [ 1.0; 0.0; 1.5 ]
    [ 2.0; 1.5; 0.0 ]
]

let backend = LocalBackendFactory.createUnified()

// solveAsync returns a Task<QuantumResult<_>>
let runTsp config (cancellationToken: CancellationToken) =
    solveAsync backend distances config cancellationToken

// Option 1: Use default configuration (optimization enabled)
task {
    match! runTsp defaultConfig CancellationToken.None with
    | Ok solution ->
        printfn "Best tour: %A" solution.Tour
        printfn "Tour length: %.2f" solution.TourLength
        printfn "Optimized parameters (gamma, beta): %A" solution.OptimizedParameters
        printfn "Optimization converged: %A" solution.OptimizationConverged    // bool option
        printfn "Iterations: %A" solution.OptimizationIterations              // int option
    | Error err -> printfn "Error: %s" err.Message
}

// Option 2: Custom configuration for fine-tuning
let customConfig = {
    OptimizationShots = 100          // Samples per step when the backend has no state vector
    FinalShots = 1000                // High shots for accurate final result
    EnableOptimization = true        // Enable variational loop
    InitialParameters = (0.5, 0.5)   // Starting guess for (gamma, beta)
    MaxOptimizationIterations = 1000 // Cap the variational loop
}
let result = runTsp customConfig CancellationToken.None

// Option 3: No variational loop at all — one circuit at the initial parameters.
// Use this when the backend is expensive (e.g. topological), since every
// optimizer iteration is a full circuit execution.
let fastResult = runTsp fastConfig CancellationToken.None
```

`QuantumTspSolver.solveAsync` is the only entry point: a fixed shot count without optimization, or the default configuration on a `LocalBackend`, are both expressed through its `QuantumTspConfig` argument.

### How QAOA Parameter Optimization Works

**Variational Quantum-Classical Loop:**
1. **Classical optimizer** proposes QAOA parameters (gamma, beta)
2. **Quantum backend** executes the QAOA circuit with those parameters
3. **Score the parameters** - the expected QUBO energy of the circuit: exact from the amplitudes on a state-vector backend, otherwise the mean over `OptimizationShots` samples
4. **Optimizer updates** parameters based on gradient-free Nelder-Mead simplex method
5. **Repeat until convergence** or until `MaxOptimizationIterations` is reached
6. **Final execution** uses optimized parameters with high shots for accurate result

**Benefits:**
- ✅ **Problem-specific parameters** - usually a higher probability of sampling good tours than fixed parameters
- ✅ **Configurable** - Easy to adjust optimization/final shots for speed vs. accuracy
- ✅ **Cheap mode** - `fastConfig` skips the variational loop entirely

**Configuration Guidelines:**
- `OptimizationShots = 100` - Samples per optimizer step on backends without a state vector (increase for noisy hardware)
- `FinalShots = 1000` - Accurate result (decrease for faster demos)
- `EnableOptimization = true` - Enable variational loop (disable for testing)
- `InitialParameters = (0.5, 0.5)` - Starting guess; the optimizer searches γ ∈ [0, π], β ∈ [0, π/2] in units of the cost Hamiltonian scaled to a largest coefficient of 1
- `MaxOptimizationIterations = 1000` - Upper bound on Nelder–Mead iterations; each one runs a full circuit

**Performance:** every optimizer iteration executes the circuit once (with `OptimizationShots` shots on hardware), so the extra cost is iterations × `OptimizationShots`. Measure on your own problem before relying on the variational loop on a paid backend.

For more details, see:
- **[Local Simulation Guide](local-simulation.md)** - Quantum simulation without Azure

## Next Steps

- **[API Reference](api-reference.md)** - Complete API documentation
- **[FAQ](faq.md)** - Frequently asked questions

## Need Help?

- **Issues:** [GitHub Issues](https://github.com/thorium/FSharp.Azure.Quantum/issues)
- **Examples:** See the [examples/](https://github.com/Thorium/FSharp.Azure.Quantum/tree/main/examples) directory
- **API Docs:** See [api-reference.md](api-reference.md)

## Authentication (for Cloud Quantum Backends)

When using cloud quantum backends (IonQ, Rigetti, Quantinuum, Atom Computing, IQM via Azure Quantum), you'll need Azure credentials. Cloud backends take an authenticated `HttpClient` and your workspace URL:

```fsharp
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Backends.CloudBackends

// Uses DefaultAzureCredential: Azure CLI (az login), Managed Identity, environment variables, etc.
let credential = Authentication.CredentialProviders.createDefaultCredential ()
let httpClient = Authentication.createAuthenticatedClient credential

let workspaceUrl =
    "https://eastus.quantum.azure.com/subscriptions/<sub>/resourceGroups/<rg>/providers/Microsoft.Quantum/workspaces/<ws>"

// httpClient, workspace URL, target, shots
let ionq = CloudBackendFactory.createIonQ httpClient workspaceUrl "ionq.simulator" 1000
```

For quota and provider queries, `FSharp.Azure.Quantum.Backends.AzureQuantumWorkspace.createDefault` takes the subscription ID, resource group, workspace name and location.

**Note:** LocalBackend (default) works without Azure credentials - suited to development, testing, and small problems that fit the simulator width.

---

**Ready to optimize!** Continue with the [API Reference](api-reference.md) for detailed documentation.
