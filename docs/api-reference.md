---
layout: default
title: API Reference
---

# API Reference

Complete reference for **FSharp.Azure.Quantum** public APIs.

## Table of Contents

- [Quick Start Patterns](#quick-start-patterns) - Common usage patterns (NEW!)
- [HybridSolver](#hybridsolver) - Automatic quantum/classical routing
- [TspSolver](#tspsolver) - Classical TSP solver
- [TspBuilder](#tspbuilder) - TSP builder pattern API  
- [PortfolioSolver](#portfoliosolver) - Classical portfolio optimization
- [PortfolioBuilder](#portfoliobuilder) - Portfolio builder pattern API
- [QuantumAdvisor](#quantumadvisor) - Decision framework for quantum vs classical
- [ProblemAnalysis](#problemanalysis) - Problem classification and complexity
- [CostEstimation](#costestimation) - Cost calculation and budget management
- [Types](#types) - Core types and data structures
- **[QUBO Encoding Strategies](qubo-encoding-strategies.md)** - Problem-specific transformations (NEW!)

---

## Quick Start Patterns

### Pattern 1: Simple Auto-Solve (Recommended)

```fsharp
open FSharp.Azure.Quantum.Classical

// TSP: Let HybridSolver decide everything
let distances = array2D [[0.0; 10.0]; [10.0; 0.0]]
match HybridSolver.solveTsp distances None None None with
| Ok solution -> printfn "Tour: %A" solution.Result.Tour
| Error msg -> eprintfn "Error: %s" msg

// Portfolio: Let HybridSolver decide everything  
let assets = [{Symbol="AAPL"; ExpectedReturn=0.12; Risk=0.18; Price=150.0}]
let constraints = {Budget=1000.0; MinHolding=0.0; MaxHolding=500.0}
match HybridSolver.solvePortfolio assets constraints None None None with
| Ok solution -> printfn "Value: $%.2f" solution.Result.TotalValue
| Error msg -> eprintfn "Error: %s" msg
```

### Pattern 2: Force Classical (Development/Testing)

```fsharp
// Force classical when developing (fast, deterministic)
let forceClassical = Some HybridSolver.Classical

match HybridSolver.solveTsp distances None None forceClassical with
| Ok solution -> 
    // Guaranteed fast result from classical solver
    printfn "Solved in %.2f ms" solution.ElapsedMs
| Error msg -> eprintfn "Error: %s" msg
```

### Pattern 3: Budget-Constrained Quantum (Production)

```fsharp
// Set budget limit for quantum execution
let budgetLimit = Some 10.0  // $10 USD max

match HybridSolver.solveTsp distances budgetLimit None None with
| Ok solution -> 
    match solution.Method with
    | Classical -> printfn "Used classical (quantum too expensive)"
    | Quantum -> printfn "Used quantum within budget"
| Error msg -> eprintfn "Error: %s" msg
```

### Pattern 4: Timeout Control

```fsharp
// Set timeout to limit execution time
let timeoutSeconds = Some 30.0  // 30 seconds max

match HybridSolver.solveTsp distances None timeoutSeconds None with
| Ok solution -> 
    printfn "Solved within timeout: %.2f ms" solution.ElapsedMs
    printfn "Tour: %A" solution.Result.Tour
| Error msg -> 
    eprintfn "Failed or timed out: %s" msg
```

### Pattern 5: Combining Budget, Timeout, and Method

```fsharp
// Full control: budget + timeout + force quantum
let budget = Some 50.0           // $50 max
let timeout = Some 60.0          // 60s max
let forceQuantum = Some HybridSolver.Quantum

match HybridSolver.solveTsp distances budget timeout forceQuantum with
| Ok solution -> 
    printfn "Method: %A" solution.Method
    printfn "Cost: (quantum execution cost)"
    printfn "Time: %.2f ms" solution.ElapsedMs
| Error msg -> eprintfn "Error: %s" msg
```

### Pattern 6: Fallback to Classical on Error

```fsharp
// Try quantum first, fallback to classical
let solveWithFallback distances =
    match HybridSolver.solveTsp distances (Some 10.0) None None with
    | Ok solution -> solution
    | Error _ -> 
        // Budget exceeded or quantum unavailable, force classical
        match HybridSolver.solveTsp distances None None (Some HybridSolver.Classical) with
        | Ok classicalSolution -> classicalSolution
        | Error msg -> failwithf "All methods failed: %s" msg

let solution = solveWithFallback distances
printfn "Method used: %A" solution.Method
printfn "Tour: %A" solution.Result.Tour
```

### Pattern 7: Validation Before Solving

```fsharp
// Validate portfolio constraints before solving
let solvePortfolioSafe assets budget =
    let constraints: PortfolioSolver.Constraints = {
        Budget = budget
        MinHolding = 0.0
        MaxHolding = budget * 0.5
    }
    
    // Validate first
    let validation = 
        PortfolioSolver.validateBudgetConstraint assets constraints
    
    if not validation.IsValid then
        Error (String.concat "; " validation.Messages)
    else
        // Validation passed, solve
        HybridSolver.solvePortfolio assets constraints None None None
        |> Result.map (fun solution -> solution.Result)
```

### Pattern 8: Understanding Quantum Recommendations

```fsharp
// Get detailed recommendation from QuantumAdvisor
match HybridSolver.solveTsp distances None None None with
| Ok solution -> 
    printfn "Method: %A" solution.Method
    printfn "Reasoning: %s" solution.Reasoning
    
    match solution.Recommendation with
    | Some rec ->
        printfn "Quantum Advantage: %s" rec.Advantage
        printfn "Estimated Cost: $%.2f" rec.EstimatedCost
        printfn "Classical Time: %.2f ms" rec.ClassicalTime
    | None -> printfn "No recommendation available"
| Error msg -> eprintfn "Error: %s" msg
```

### Pattern 9: Parallel Solves (Advanced)

```fsharp
open System.Threading.Tasks

// Solve multiple problems in parallel
let problems = [distances1; distances2; distances3]

let solutions = 
    problems
    |> List.map (fun distances ->
        Task.Run(fun () ->
            HybridSolver.solveTsp distances None None None))
    |> Task.WhenAll
    |> fun task -> task.Result

// Process results
solutions 
|> Array.iteri (fun i result ->
    match result with
    | Ok sol -> printfn "Problem %d: %.2f" i sol.Result.TourLength
    | Error msg -> eprintfn "Problem %d failed: %s" i msg)
```

### Pattern 10: Portfolio Optimization with HybridSolver

```fsharp
// Portfolio optimization with automatic quantum routing
let assets = [
    {Symbol="AAPL"; ExpectedReturn=0.12; Risk=0.18; Price=150.0}
    {Symbol="MSFT"; ExpectedReturn=0.10; Risk=0.15; Price=300.0}
    {Symbol="GOOGL"; ExpectedReturn=0.15; Risk=0.22; Price=2800.0}
]

let constraints = {
    Budget = 100000.0       // $100k investment
    MinHolding = 1000.0     // Min $1k per asset
    MaxHolding = 50000.0    // Max $50k per asset
}

match HybridSolver.solvePortfolio assets constraints None None None with
| Ok solution ->
    printfn "Method: %A" solution.Method
    printfn "Total Value: $%.2f" solution.Result.TotalValue
    printfn "Expected Return: %.2f%%" (solution.Result.ExpectedReturn * 100.0)
    printfn "Risk: %.2f%%" (solution.Result.Risk * 100.0)
| Error msg -> eprintfn "Error: %s" msg
```

---

## HybridSolver

Automatically routes optimization problems to quantum or classical solvers based on problem characteristics and cost considerations.

### Types

```fsharp
type SolverMethod =
    | Classical
    | Quantum

type Solution<'TResult> = {
    Method: SolverMethod
    Result: 'TResult
    Reasoning: string
    ElapsedMs: float
    Recommendation: QuantumAdvisor.Recommendation option
}
```

### Functions

#### `solveTsp`

Solve TSP problem with automatic quantum vs classical selection.

```fsharp
val solveTsp : 
    distances: float[,] ->
    budget: float option ->
    timeout: float option ->
    forceMethod: SolverMethod option ->
    Result<Solution<TspSolver.TspSolution>, string>
```

**Parameters:**
- `distances` - NxN distance matrix for N cities
- `budget` - Optional budget limit for quantum execution (USD)
- `timeout` - Optional timeout for classical solver (milliseconds)
- `forceMethod` - Optional override to force Classical or Quantum

**Example:**
```fsharp
// Automatic selection
let result = HybridSolver.solveTsp distances None None None

// Force classical
let result = HybridSolver.solveTsp distances None None (Some Classical)

// With budget limit
let result = HybridSolver.solveTsp distances (Some 10.0) None None
```

#### `solvePortfolio`

Solve portfolio optimization with automatic routing.

```fsharp
val solvePortfolio :
    assets: PortfolioSolver.Asset list ->
    constraints: PortfolioSolver.Constraints ->
    budget: float option ->
    timeout: float option ->
    forceMethod: SolverMethod option ->
    Result<Solution<PortfolioSolver.PortfolioSolution>, string>
```

**Example:**
```fsharp
let result = HybridSolver.solvePortfolio assets constraints None None None
```

---

## TspSolver

Classical Traveling Salesman Problem solver using local search optimization.

### Types

```fsharp
type TspConfig = {
    MaxIterations: int
    InitialTemperature: float
    CoolingRate: float
    RandomSeed: int option
}

type TspSolution = {
    Tour: int array
    TourLength: float
    Iterations: int
}
```

### Functions

#### `solveWithDistances`

Solve TSP given a distance matrix.

```fsharp
val solveWithDistances : 
    distances: float[,] ->
    config: TspConfig ->
    TspSolution
```

**Example:**
```fsharp
let config = TspSolver.defaultConfig
let solution = TspSolver.solveWithDistances distances config
printfn "Tour: %A, Length: %.2f" solution.Tour solution.TourLength
```

#### `defaultConfig`

Default configuration for TSP solver.

```fsharp
val defaultConfig : TspConfig
// MaxIterations = 10000
// InitialTemperature = 100.0
// CoolingRate = 0.995
// RandomSeed = None
```

---

## TSP (TspBuilder)

Domain builder API for constructing and solving TSP problems with named cities.

### Types

```fsharp
type City = {
    Name: string
    X: float
    Y: float
}

type Tour = {
    Cities: string list
    TotalDistance: float
    IsValid: bool
}
```

### Functions

#### `createProblem`

Create TSP problem from list of cities with coordinates.

```fsharp
val createProblem : cities: (string * float * float) list -> TspProblem
```

**Parameters:**
- `cities` - List of (name, x-coordinate, y-coordinate) tuples

#### `solve`

Solve TSP problem using classical algorithm.

```fsharp
val solve : problem: TspProblem -> config: TspConfig option -> Result<Tour, string>
```

#### `solveDirectly`

Create and solve TSP in one step.

```fsharp
val solveDirectly : cities: (string * float * float) list -> config: TspConfig option -> Result<Tour, string>
```

**Example:**
```fsharp
let cities = [("Seattle", 0.0, 0.0); ("Portland", 0.0, 174.0); ("SF", 635.0, 807.0)]
let problem = TSP.createProblem cities
match TSP.solve problem None with
| Ok tour -> printfn "Distance: %.2f" tour.TotalDistance
| Error msg -> printfn "Error: %s" msg

// Or solve directly
match TSP.solveDirectly cities None with
| Ok tour -> printfn "Cities: %A" tour.Cities
| Error msg -> printfn "Error: %s" msg
```

---

## PortfolioSolver

Classical portfolio optimization solver using greedy and constraint-based approaches.

### Types

```fsharp
type Asset = {
    Symbol: string
    ExpectedReturn: float
    Risk: float
    Price: float
}

type Constraints = {
    Budget: float
    MinHolding: float
    MaxHolding: float
}

type PortfolioSolution = {
    Allocations: (string * float) list
    TotalValue: float
    ExpectedReturn: float
    TotalRisk: float
}
```

### Functions

#### `solveGreedyByRatio`

Solve portfolio optimization using return/risk ratio greedy approach.

```fsharp
val solveGreedyByRatio :
    assets: PortfolioTypes.Asset list ->
    constraints: Constraints ->
    config: PortfolioConfig ->
    PortfolioSolution
```

**Example:**
```fsharp
let assets = [
    { Symbol = "AAPL"; ExpectedReturn = 0.12; Risk = 0.18; Price = 150.0 }
    { Symbol = "MSFT"; ExpectedReturn = 0.10; Risk = 0.15; Price = 300.0 }
]

let constraints: PortfolioSolver.Constraints = {
    Budget = 10000.0
    MinHolding = 0.0
    MaxHolding = 5000.0
}

let solution = PortfolioSolver.solveGreedyByRatio assets constraints PortfolioSolver.defaultConfig
```

---

## Portfolio (PortfolioBuilder)

Domain builder API for constructing and solving portfolio optimization problems.

### Types

```fsharp
type Asset = {
    Symbol: string
    ExpectedReturn: float
    Risk: float
    Price: float
}

type PortfolioAllocation = {
    Allocations: (string * float * float) list  // (symbol, shares, value)
    TotalValue: float
    ExpectedReturn: float
    Risk: float
    IsValid: bool
}
```

### Functions

#### `createProblem`

Create portfolio problem from assets and budget.

```fsharp
val createProblem : assets: (string * float * float * float) list -> budget: float -> PortfolioProblem
```

**Parameters:**
- `assets` - List of (symbol, expectedReturn, risk, price) tuples
- `budget` - Total budget available for investment

#### `solve`

Solve portfolio optimization problem.

```fsharp
val solve : problem: PortfolioProblem -> config: PortfolioConfig option -> Result<PortfolioAllocation, string>
```

#### `solveDirectly`

Create and solve portfolio in one step.

```fsharp
val solveDirectly : assets: (string * float * float * float) list -> budget: float -> config: PortfolioConfig option -> Result<PortfolioAllocation, string>
```

**Example:**
```fsharp
let assets = [
    ("AAPL", 0.12, 0.18, 150.0)   // symbol, return, risk, price
    ("MSFT", 0.10, 0.15, 300.0)
    ("GOOGL", 0.15, 0.20, 2800.0)
]

let problem = Portfolio.createProblem assets 10000.0
match Portfolio.solve problem None with
| Ok allocation -> 
    printfn "Total Value: $%.2f" allocation.TotalValue
    printfn "Expected Return: %.2f%%" (allocation.ExpectedReturn * 100.0)
| Error msg -> printfn "Error: %s" msg

// Or solve directly
match Portfolio.solveDirectly assets 10000.0 None with
| Ok allocation -> printfn "Allocations: %A" allocation.Allocations
| Error msg -> printfn "Error: %s" msg
```

---

## QuantumAdvisor

Decision framework for determining when to use quantum vs classical approaches.

### Types

```fsharp
type RecommendationType =
    | StronglyRecommendClassical
    | ConsiderQuantum
    | StronglyRecommendQuantum

type Recommendation = {
    RecommendationType: RecommendationType
    ProblemSize: int
    Complexity: string
    EstimatedClassicalTime: float
    EstimatedQuantumCost: float option
    Reasoning: string
}
```

### Functions

#### `getRecommendation`

Get quantum vs classical recommendation for a problem.

```fsharp
val getRecommendation : problem: 'T -> Result<Recommendation, string>
```

**Example:**
```fsharp
match QuantumAdvisor.getRecommendation distances with
| Ok recommendation ->
    printfn "Recommendation: %A" recommendation.RecommendationType
    printfn "Reasoning: %s" recommendation.Reasoning
| Error msg -> printfn "Error: %s" msg
```

---

## ProblemAnalysis

Problem classification and complexity analysis for optimization problems.

### Types

```fsharp
type ProblemType =
    | TSP
    | Portfolio
    | QUBO
    | Unknown

type ProblemInfo = {
    ProblemType: ProblemType
    Size: int
    Complexity: string
    SearchSpaceSize: float
    IsSymmetric: bool
    Density: float
}
```

### Functions

#### `classifyProblem`

Classify and analyze an optimization problem.

```fsharp
val classifyProblem : input: 'T -> Result<ProblemInfo, string>
```

---

## CostEstimation

Cost calculation and estimation for quantum backend execution.

### Types

```fsharp
type PricingTier =
    | Free
    | Standard
    | Premium

type CostEstimate = {
    EstimatedCost: float
    Currency: string
    Breakdown: Map<string, float>
}
```

### Functions

#### `estimateQuantumCost`

Estimate cost for quantum execution.

```fsharp
val estimateQuantumCost : 
    problemSize: int ->
    shots: int ->
    tier: PricingTier ->
    CostEstimate
```

---

## Types

Core types and data structures used throughout the library.

### Workspace Configuration

```fsharp
type WorkspaceConfig = {
    SubscriptionId: string
    ResourceGroup: string
    WorkspaceName: string
    Location: string
}
```

### Result Types

All functions return `Result<'T, string>` for error handling:
- `Ok value` - Success with result
- `Error message` - Failure with error message

---

## Error Handling

All API functions use F#'s `Result<'T, string>` type for error handling:

```fsharp
match HybridSolver.solveTsp distances None None None with
| Ok solution -> 
    // Success - use solution
    printfn "Solution: %A" solution.Result
| Error errorMessage ->
    // Failure - handle error
    printfn "Error: %s" errorMessage
```

---

## Authentication

The library uses Azure authentication via `DefaultAzureCredential`:

```fsharp
// Supports (in order):
// 1. Environment variables (AZURE_CLIENT_ID, AZURE_TENANT_ID, AZURE_CLIENT_SECRET)
// 2. Azure CLI (`az login`)
// 3. Managed Identity (when running in Azure)
// 4. Visual Studio / VS Code authentication
```

**No authentication needed for classical solvers!**

---

## See Also

- [Getting Started Guide](getting-started.md)
- [TSP Example](examples/tsp-example.md)
- [Portfolio Example](examples/portfolio-example.md)
- [Hybrid Solver Guide](examples/hybrid-solver.md)
