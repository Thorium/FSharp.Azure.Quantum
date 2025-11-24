# API Reference

Complete reference for **FSharp.Azure.Quantum** public APIs.

## Table of Contents

- [HybridSolver](#hybridsolver) - Automatic quantum/classical routing
- [TspSolver](#tspsolver) - Classical TSP solver
- [TspBuilder](#tspbuilder) - TSP builder pattern API  
- [PortfolioSolver](#portfoliosolver) - Classical portfolio optimization
- [PortfolioBuilder](#portfoliobuilder) - Portfolio builder pattern API
- [QuantumAdvisor](#quantumadvisor) - Decision framework for quantum vs classical
- [ProblemAnalysis](#problemanalysis) - Problem classification and complexity
- [CostEstimation](#costestimation) - Cost calculation and budget management
- [Types](#types) - Core types and data structures

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

## TspBuilder

Builder pattern API for constructing and solving TSP problems.

### Functions

#### `createProblem`

Create a new TSP problem builder.

```fsharp
val createProblem : unit -> TspProblem
```

#### `solve`

Solve a TSP problem with default configuration.

```fsharp
val solve : TspProblem -> TspSolver.TspSolution
```

#### `solveDirectly`

Solve a TSP problem with custom configuration.

```fsharp
val solveDirectly : config: TspConfig -> problem: TspProblem -> TspSolver.TspSolution
```

**Example:**
```fsharp
let problem = Tsp.createProblem()
let solution = Tsp.solve problem
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
    assets: Asset list ->
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

let constraints = {
    Budget = 10000.0
    MinHolding = 0.0
    MaxHolding = 5000.0
}

let solution = PortfolioSolver.solveGreedyByRatio assets constraints PortfolioSolver.defaultConfig
```

---

## PortfolioBuilder

Builder pattern API for constructing and solving portfolio optimization problems.

### Functions

#### `createProblem`

Create a new portfolio problem builder.

```fsharp
val createProblem : unit -> PortfolioProblem
```

#### `solve`

Solve portfolio with default configuration.

```fsharp
val solve : PortfolioProblem -> PortfolioSolver.PortfolioSolution
```

#### `solveDirectly`

Solve portfolio with custom configuration.

```fsharp
val solveDirectly : config: PortfolioConfig -> problem: PortfolioProblem -> PortfolioSolver.PortfolioSolution
```

**Example:**
```fsharp
let problem = Portfolio.createProblem()
let solution = Portfolio.solve problem
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
