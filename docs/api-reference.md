---
layout: default
title: API Reference
---

# API Reference

Complete reference for **FSharp.Azure.Quantum** quantum optimization APIs.

## Table of Contents

**Business Optimization APIs:**
- [Quick Start Patterns](#quick-start-patterns) - Common usage patterns
- [Graph Coloring Builder](#graph-coloring-builder) - Register allocation, scheduling
- [MaxCut Builder](#maxcut-builder) - Circuit partitioning, community detection
- [Knapsack Builder](#knapsack-builder) - Resource allocation, cargo loading
- [TSP Builder](#tsp-builder) - Route optimization, delivery planning
- [Portfolio Builder](#portfolio-builder) - Investment allocation, asset selection
- [Network Flow Builder](#network-flow-builder) - Supply chain optimization
- [HybridSolver](#hybridsolver) - Classical/quantum routing by problem size
- [Task Scheduling API](TaskScheduling-API) and [Graph Coloring API](GraphColoring-API) - Separate detailed pages

**Quantum Algorithm APIs (Research & Education):**
- [Quantum Linear System Solver](#quantum-linear-system-solver-hhl-algorithm) - HHL algorithm for Ax = b

**QAOA Execution & Decomposition:**
- [QAOA Execution Helpers](#qaoa-execution-helpers) - Unified QAOA execution, sparse QUBO, budget control
- [Problem Decomposition](#problem-decomposition) - Backend-aware problem splitting and graph decomposition

**Infrastructure:**
- [Quantum Backends](#quantum-backends) - LocalBackend, IonQ, Rigetti, IQubitLimitedBackend
- [C# Interop](#c-interop) - Using from C#
- [Core Types](#core-types) - Data structures and result types
- **[QUBO Encoding Strategies](qubo-encoding-strategies.md)** - Problem transformations

---

## Error Handling

**The solver APIs use `QuantumResult<'T>` with structured `QuantumError` values** (namespace `FSharp.Azure.Quantum.Core`):

```text
type QuantumResult<'T> = Result<'T, QuantumError>
```

(A few classical helpers return `Result<'T, string>` instead; their signatures below say so.)

### Basic Error Handling

The problem builders return `Task<QuantumResult<'T>>` for consistent, type-safe error handling:

```fsharp
open System.Threading
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.GraphColoring

let problem = graphColoring {
    node "R1" ["R2"; "R3"]
    node "R2" ["R1"]
    node "R3" ["R1"]
    colors ["Red"; "Blue"; "Green"]
}

task {
    match! GraphColoring.solveAsync problem 3 None CancellationToken.None with
    | Ok solution -> 
        printfn "Success! Colors used: %d" solution.ColorsUsed
    | Error err -> 
        printfn "Error: %s" err.Message  // Human-readable message
}
```

### QuantumError Types

Errors are categorized for precise handling (`[<RequireQualifiedAccess>]`, so write `QuantumError.ValidationError`):

```text
type QuantumError =
    | ValidationError of field: string * reason: string
    | NotImplemented of feature: string * hint: string option
    | OperationError of operation: string * context: string
    | BackendError of backend: string * reason: string
    | AzureError of AzureQuantumError
    | IOError of operation: string * path: string * reason: string
    | Other of message: string

    member Message : string    // human-readable text
    member Category : string   // "Validation", "Operation", "Backend", ...
```

### Advanced Error Handling

Pattern match on error types for custom handling:

```fsharp
let cities = TSP.createProblem [ ("A", 0.0, 0.0); ("B", 1.0, 0.0); ("C", 0.5, 1.0) ]

task {
    match! TSP.solveAsync cities None CancellationToken.None with
    | Ok tour -> printfn "Tour: %A" tour.Cities
    | Error (QuantumError.ValidationError (field, reason)) ->
        printfn "Invalid %s: %s" field reason
    | Error (QuantumError.BackendError (backend, reason)) ->
        printfn "Backend %s failed: %s" backend reason
        // Retry with different backend
    | Error err ->
        printfn "Unexpected error: %s" err.Message
}
```

### Computation Expression (Recommended)

Use the `quantumResultTask` builder (from `FSharp.Azure.Quantum.Core`, the Task-based twin of `quantumResult`) to avoid nested match clauses:

```fsharp
let colorsNeeded (problem: GraphColoringProblem) (cancellationToken: CancellationToken) = quantumResultTask {
    do! GraphColoring.validate problem
    let! solution = GraphColoring.solveAsync problem 3 None cancellationToken
    if solution.IsValid then
        return solution.ColorsUsed
    else
        return! Error (QuantumError.OperationError ("coloring", "solution has conflicts"))
}
```

See [QuantumResult Builder Guide](quantumresult-builder-guide.md) for complete details.

---

## Quick Start Patterns

### Pattern 1: Simple Auto-Solve (Recommended)

```fsharp
open System.Threading
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.GraphColoring

// Graph Coloring: Uses LocalBackend automatically
let problem = graphColoring {
    node "R1" ["R2"; "R3"]
    node "R2" ["R1"]
    node "R3" ["R1"]
    colors ["Red"; "Blue"; "Green"]
}

task {
    match! GraphColoring.solveAsync problem 3 None CancellationToken.None with
    | Ok solution -> 
        printfn "Colors used: %d" solution.ColorsUsed
        printfn "Valid: %b" solution.IsValid
    | Error err -> 
        printfn "Error: %s" err.Message
}
```

### Pattern 2: Cloud Backend

```fsharp
open FSharp.Azure.Quantum.Backends.CloudBackends

// Create an Azure Quantum backend (authenticated HttpClient + workspace URL)
let credential = Authentication.CredentialProviders.createDefaultCredential ()
let httpClient = Authentication.createAuthenticatedClient credential
let workspaceUrl = "https://eastus.quantum.azure.com/subscriptions/<sub>/resourceGroups/<rg>/providers/Microsoft.Quantum/workspaces/<ws>"

let cloudBackend = CloudBackendFactory.createIonQ httpClient workspaceUrl "ionq.simulator" 1000

// Solve on the cloud backend
task {
    match! GraphColoring.solveAsync problem 3 (Some cloudBackend) CancellationToken.None with
    | Ok solution -> 
        printfn "Colors used: %d" solution.ColorsUsed
        printfn "Valid: %b" solution.IsValid
    | Error err -> 
        printfn "Error: %s" err.Message
}
```

### Pattern 3: Inspect Solution Details

```fsharp
// Solve and inspect detailed results
task {
    match! GraphColoring.solveAsync problem 3 None CancellationToken.None with
    | Ok solution -> 
        printfn "Solution found!"
        printfn "  Colors used: %d" solution.ColorsUsed
        printfn "  Conflicts: %d" solution.ConflictCount
        printfn "  Valid: %b" solution.IsValid
        
        // Print color assignments
        solution.Assignments
        |> Map.iter (fun node color ->
            printfn "  %s -> %s" node color
        )
    | Error err -> printfn "Error: %s" err.Message
}
```

---

## Graph Coloring Builder

**Module:** `FSharp.Azure.Quantum.GraphColoring`

**Use Cases:**
- Register allocation in compilers
- Frequency assignment for cell towers
- Exam scheduling (no student conflicts)
- Task scheduling with resource conflicts

### Computation Expression API

```fsharp
let problem_scheduling = graphColoring {
    // Define nodes with conflicts
    node "Task1" ["Task2"; "Task3"]
    node "Task2" ["Task1"; "Task4"]
    node "Task3" ["Task1"]
    node "Task4" ["Task2"]
    
    // Available colors/resources
    colors ["Slot A"; "Slot B"; "Slot C"]
    
    // Optimization objective
    objective MinimizeColors  // or MinimizeConflicts, BalanceColors
}
```

### Types

```text
type ColoredNode = {
    Id: string
    ConflictsWith: string list
    FixedColor: string option         // Pre-assigned color
    Priority: float                   // Tie-breaking priority
    AvoidColors: string list          // Soft constraints
    Properties: Map<string, obj>      // Custom metadata
}

type ColoringObjective =
    | MinimizeColors      // Minimize chromatic number
    | MinimizeConflicts   // Allow invalid colorings, minimize violations
    | BalanceColors       // Load balancing

type GraphColoringProblem = {
    Nodes: ColoredNode list
    AvailableColors: string list
    Objective: ColoringObjective
    MaxColors: int option
    ConflictPenalty: float
}

type ColoringSolution = {
    Assignments: Map<string, string>   // Node → Color mapping
    ColorsUsed: int
    ConflictCount: int
    IsValid: bool
    ColorDistribution: Map<string, int>
    Cost: float
    BackendName: string
    IsQuantum: bool
}
```

### Functions

```text
val validate : GraphColoringProblem → QuantumResult<unit>
val solveAsync : GraphColoringProblem → int → IQuantumBackend option → CancellationToken → Task<QuantumResult<ColoringSolution>>
val node : string → string list → ColoredNode
```

**Parameters of `solveAsync`:**
- `problem` - Graph coloring problem specification
- `numColors` - Number of colors the QAOA encoding uses (capped at the number of available colors and `MaxColors`); qubits needed = nodes × encoded colors
- `backend` - Quantum backend (None = new LocalBackend)
- `cancellationToken` - Cancels the backend execution

**Computation expression operations:** `node id conflicts`, `nodes [ColoredNode list]`, `colors [...]` (required), `objective`, `maxColors`, `conflictPenalty`. The builder validates the problem when it finishes and throws if it is invalid (no nodes, no colors, unknown conflict references, ...).

> **Note:** `solveAsync` encodes every option: `ConflictPenalty` multiplies the conflict penalty, `MaxColors` limits the encoded colors to the first `MaxColors`, `AvoidColors` and `Objective` add soft QUBO terms and pick among the samples, and `Priority` breaks ties. A graph with no conflicts runs no circuit (`IsQuantum = false`). See [Graph Coloring API](GraphColoring-API.md) for the weights.

### Example

```fsharp
// Register allocation for compiler
let registers = graphColoring {
    // Variables that interfere (live at same time)
    node "x" ["y"; "z"]
    node "y" ["x"; "w"]
    node "z" ["x"; "w"]
    node "w" ["y"; "z"]
    
    // Available CPU registers
    colors ["EAX"; "EBX"; "ECX"; "EDX"]
    
    objective MinimizeColors
}

task {
    match! GraphColoring.solveAsync registers 4 None CancellationToken.None with
    | Ok solution ->
        printfn "Registers needed: %d" solution.ColorsUsed
        solution.Assignments 
        |> Map.iter (fun var reg -> printfn "%s → %s" var reg)
    | Error err ->
        printfn "Allocation failed: %s" err.Message
}
```

---

## MaxCut Builder

**Module:** `FSharp.Azure.Quantum.MaxCut`

**Use Cases:**
- Circuit partitioning (minimize wire crossings)
- Community detection in social networks
- Load balancing across servers
- Image segmentation

### Functions

```text
val createProblem : string list → (string * string * float) list → MaxCutProblem
val completeGraph : string list → float → MaxCutProblem
val cycleGraph : string list → float → MaxCutProblem
val pathGraph : string list → float → MaxCutProblem
val gridGraph : int → int → float → MaxCutProblem
val starGraph : string → string list → float → MaxCutProblem
val solveAsync : MaxCutProblem → IQuantumBackend option → CancellationToken → Task<QuantumResult<Solution>>
val calculateCutValue : MaxCutProblem → string list → float
```

`solveWithAdaptQaoa` runs the adaptive (ADAPT-QAOA) variant. Qubits needed: one per vertex.

### Types

```text
type MaxCutProblem = {
    Vertices: string list
    Edges: Edge<float> list
    VertexCount: int
    EdgeCount: int
}

type Solution = {
    PartitionS: string list         // First partition
    PartitionT: string list         // Second partition
    CutValue: float                 // Total edge weight crossing partition
    CutEdges: Edge<float> list      // Edges in the cut
    BackendName: string
    IsQuantum: bool
}
```

### Example

```fsharp
// Network partitioning
let vertices = ["Server1"; "Server2"; "Server3"; "Server4"]
let edges = [
    ("Server1", "Server2", 10.0)  // communication cost
    ("Server2", "Server3", 5.0)
    ("Server3", "Server4", 8.0)
    ("Server4", "Server1", 3.0)
    ("Server1", "Server3", 12.0)
]

let problem_maxcut = MaxCut.createProblem vertices edges

task {
    match! MaxCut.solveAsync problem_maxcut None CancellationToken.None with
    | Ok solution ->
        printfn "Partition 1: %A" solution.PartitionS
        printfn "Partition 2: %A" solution.PartitionT
        printfn "Inter-partition traffic: %.2f" solution.CutValue
    | Error err ->
        printfn "Partitioning failed: %s" err.Message
}
```

---

## Knapsack Builder

**Module:** `FSharp.Azure.Quantum.Knapsack`

**Use Cases:**
- Resource allocation within budget
- Cargo loading optimization
- Project selection with constraints
- Portfolio construction

### Functions

```text
val createProblem : (string * float * float) list → float → Problem
val solveAsync : Problem → IQuantumBackend option → CancellationToken → Task<QuantumResult<Solution>>
```

**Parameters:**
- `items` - (id, weight, value) tuples
- `capacity` - Maximum total weight

Qubits needed: one per item. Ready-made problems: `budgetAllocation`, `cargoLoading`, `taskScheduling`, `randomInstance`.

### Types

```text
type Item = QuantumKnapsackSolver.KnapsackItem   // { Id: string; Weight: float; Value: float }

type Problem = {
    Items: Item list
    Capacity: float
    ItemCount: int
    TotalValue: float
    TotalWeight: float
}

type Solution = {
    SelectedItems: Item list
    TotalWeight: float
    TotalValue: float
    IsFeasible: bool
    Efficiency: float                // Value per unit weight
    CapacityUtilization: float       // Percentage used
    BackendName: string
    IsQuantum: bool
}
```

### Example

```fsharp
// Cargo loading optimization
let cargo = [
    ("Electronics", 50.0, 10000.0)
    ("Furniture", 200.0, 5000.0)
    ("Textiles", 30.0, 3000.0)
    ("Machinery", 150.0, 8000.0)
    ("Food", 80.0, 2000.0)
]

let problem_knapsack = Knapsack.createProblem cargo 300.0  // 300kg capacity

task {
    match! Knapsack.solveAsync problem_knapsack None CancellationToken.None with
    | Ok solution ->
        printfn "Total value: $%.2f" solution.TotalValue
        printfn "Weight: %.2f/%.2f kg" solution.TotalWeight problem_knapsack.Capacity
        printfn "Efficiency: $%.2f/kg" solution.Efficiency
        
        solution.SelectedItems 
        |> List.iter (fun item -> 
            printfn "  Load: %s (%.2f kg, $%.2f)" item.Id item.Weight item.Value)
    | Error err ->
        printfn "Optimization failed: %s" err.Message
}
```

---

## TSP Builder

**Module:** `FSharp.Azure.Quantum.TSP`

**Use Cases:**
- Delivery route optimization
- PCB drilling path planning
- Logistics and supply chain
- Robot path planning

### Functions

```text
val createProblem : (string * float * float) list → TspProblem
val solveAsync : TspProblem → IQuantumBackend option → CancellationToken → Task<QuantumResult<Tour>>
```

**Parameters:**
- `cities` - (name, x, y) coordinate tuples; distances are Euclidean

Qubits needed: cities² (4 cities = 16 qubits), so QAOA on the local simulator is limited to a handful of cities. For larger instances use `HybridSolver.solveTspAsync`, whose classical path has no qubit limit.

### Types

```text
type City = TspTypes.City   // { Name: string option; X: float; Y: float }

type TspProblem = {
    Cities: City array
    CityCount: int
    DistanceMatrix: float[,]
}

type Tour = {
    Cities: string list             // City names in tour order
    TotalDistance: float
    IsValid: bool
}
```

### Example

```fsharp
// Delivery route optimization (4 stops = 16 qubits)
let stops = [
    ("Warehouse", 0.0, 0.0)
    ("Customer A", 5.0, 3.0)
    ("Customer B", 2.0, 7.0)
    ("Customer C", 8.0, 4.0)
]

let problem_tsp = TSP.createProblem stops

task {
    match! TSP.solveAsync problem_tsp None CancellationToken.None with
    | Ok tour ->
        printfn "Optimal route: %s" (String.concat " → " tour.Cities)
        printfn "Total distance: %.2f km" tour.TotalDistance
    | Error err ->
        printfn "Route optimization failed: %s" err.Message
}
```

---

## Portfolio Builder

**Module:** `FSharp.Azure.Quantum.Portfolio`

**Use Cases:**
- Investment portfolio allocation
- Asset selection with budget constraints
- Risk-return optimization
- Capital allocation

### Functions

```text
val createProblem : (string * float * float * float) list → float → PortfolioProblem
val createProblemWithCovariance : (string * float * float * float) list → float → float[,] → PortfolioProblem
val createProblemWithCorrelation : (string * float * float * float) list → float → float[,] → QuantumResult<PortfolioProblem>
val solveAsync : PortfolioProblem → IQuantumBackend option → CancellationToken → Task<QuantumResult<PortfolioAllocation>>
```

**Parameters:**
- `assets` - (symbol, expectedReturn, risk, price) tuples
- `budget` - Total available capital
- `covariance` - Covariance Σ of the asset returns, rows and columns in asset order
- `correlation` - Correlation ρ; the covariance is Σᵢⱼ = ρᵢⱼ × riskᵢ × riskⱼ

Qubits needed: one per asset.

**Risk and the objective.** Without a covariance the assets are treated as independent and risk = sqrt(Σ (wᵢσᵢ)²), σᵢ = `Risk`. With one, risk = sqrt(wᵀΣw) and the QUBO carries the covariance terms. `solveAsync` returns a `ValidationError` for a covariance that is not square, not one row per asset, not symmetric or not positive semidefinite (tolerance `PortfolioTypes.CovarianceTolerance` × the largest variance).

The QUBO is the discretised mean-variance problem: selecting asset i buys one lot of weight s of the budget, so w = s·x, and QAOA minimises −μᵀw + λ wᵀΣw (λ = risk aversion, 0.5 in `solveAsync`). s is 1/n, raised to MinHolding/Budget or lowered to MaxHolding/Budget when the holding limits require it; with s > 1/n at most ⌊1/s⌋ assets fit the budget. Budget not bought stays uninvested. Shares may be fractional, but an asset is only bought when one lot covers at least one share (the classical greedy has the same rule); unaffordable assets get no qubit. The solver samples p = 1 QAOA (cost Hamiltonian scaled to a largest coefficient of 1 by the shared pipeline) on a grid of angles with γ > 0, the minimising sign, samples again at the angles with the lowest mean energy, and returns the best feasible sample. `QuantumPortfolioSolver.toQubo` builds the QUBO with `ProblemTransformer.encodePortfolioCorrelation`; `QuantumPortfolioSolver.solveWithCovarianceAsync` is the algorithm-level entry point.

`PortfolioTypes` also has `validateCovariance`, `covarianceFromCorrelation`, `portfolioVariance` and `portfolioRisk`.

### Types

```text
type Asset = PortfolioTypes.Asset   // { Symbol: string; ExpectedReturn: float; Risk: float; Price: float }

type PortfolioProblem = {
    Assets: Asset array
    AssetCount: int
    Budget: float
    Constraints: PortfolioSolver.Constraints option   // { Budget; MinHolding; MaxHolding }
    Covariance: float[,] option                       // None = independent assets
}

type PortfolioAllocation = {
    Allocations: (string * float * float) list  // (symbol, shares, value)
    TotalValue: float
    ExpectedReturn: float
    Risk: float
    IsValid: bool
}
```

### Example

```fsharp
// Investment allocation
let assets = [
    ("AAPL", 0.12, 0.15, 150.0)      // return, risk, price
    ("GOOGL", 0.10, 0.12, 2800.0)
    ("MSFT", 0.11, 0.14, 350.0)
    ("BONDS", 0.05, 0.03, 100.0)
]

let problem_portfolio = Portfolio.createProblem assets 50000.0  // $50k budget

task {
    match! Portfolio.solveAsync problem_portfolio None CancellationToken.None with
    | Ok allocation ->
        printfn "Portfolio value: $%.2f" allocation.TotalValue
        printfn "Expected return: %.2f%%" (allocation.ExpectedReturn * 100.0)
        printfn "Portfolio risk: %.2f" allocation.Risk
        
        allocation.Allocations 
        |> List.iter (fun (symbol, shares, value) ->
            printfn "  %s: %.2f shares = $%.2f" symbol shares value)
    | Error err ->
        printfn "Allocation failed: %s" err.Message
}

// With correlations between the assets
let correlation =
    array2D [
        [ 1.0; 0.6; 0.7; 0.0 ]
        [ 0.6; 1.0; 0.8; 0.0 ]
        [ 0.7; 0.8; 1.0; 0.0 ]
        [ 0.0; 0.0; 0.0; 1.0 ]
    ]

task {
    match Portfolio.createProblemWithCorrelation assets 50000.0 correlation with
    | Ok correlated ->
        match! Portfolio.solveAsync correlated None CancellationToken.None with
        | Ok allocation -> printfn "Risk with correlations: %.4f" allocation.Risk
        | Error err -> printfn "Allocation failed: %s" err.Message
    | Error err -> printfn "Invalid correlation: %s" err.Message
}
```

---

## Network Flow Builder

**Module:** `FSharp.Azure.Quantum.NetworkFlow`

**Use Cases:**
- Supply chain optimization
- Distribution network design
- Transportation planning
- Manufacturing flow optimization

### Types

```text
type NodeType =
    | Source        // Supplier, factory
    | Sink          // Customer, demand point
    | Intermediate  // Warehouse, distribution center

type Node = {
    Id: string
    NodeType: NodeType
    Capacity: int
    Demand: int option      // Sinks only
    Supply: int option      // Sources only
}

type Route = {
    From: string
    To: string
    Cost: float
}

type NetworkFlowProblem = { Nodes: Node list; Routes: Route list }

type FlowSolution = {
    SelectedRoutes: (string * string * float) list
    TotalCost: float
    DemandSatisfied: float
    TotalDemand: float
    FillRate: float
    IsValid: bool
    BackendName: string
}
```

### Helper Functions

```text
val createSource : id:string → supply:int → capacity:int → Node
val createSink : id:string → demand:int → Node
val createIntermediate : id:string → capacity:int → Node
val createRoute : from:string → to_:string → cost:float → Route
val createProblem : Node list → Route list → NetworkFlowProblem
val solveAsync : NetworkFlowProblem → IQuantumBackend option → CancellationToken → Task<QuantumResult<FlowSolution>>
val solveDirectlyAsync : Node list → Route list → IQuantumBackend option → CancellationToken → Task<QuantumResult<FlowSolution>>
```

Qubits needed: one per route.

### Example

```fsharp
// Supply chain optimization
let nodes = [
    NetworkFlow.createSource "Factory A" 1000 1000
    NetworkFlow.createSource "Factory B" 800 800
    NetworkFlow.createIntermediate "Warehouse" 1500
    NetworkFlow.createSink "Store 1" 400
    NetworkFlow.createSink "Store 2" 600
    NetworkFlow.createSink "Store 3" 300
]

let routes = [
    NetworkFlow.createRoute "Factory A" "Warehouse" 5.0
    NetworkFlow.createRoute "Factory B" "Warehouse" 4.0
    NetworkFlow.createRoute "Warehouse" "Store 1" 3.0
    NetworkFlow.createRoute "Warehouse" "Store 2" 2.5
    NetworkFlow.createRoute "Warehouse" "Store 3" 4.5
]

let flowProblem = NetworkFlow.createProblem nodes routes

task {
    match! NetworkFlow.solveAsync flowProblem None CancellationToken.None with
    | Ok flow ->
        printfn "Total cost: $%.2f" flow.TotalCost
        printfn "Fill rate: %.1f%%" (flow.FillRate * 100.0)
        
        flow.SelectedRoutes 
        |> List.iter (fun (from, to_, amount) ->
            printfn "  %s → %s: %.2f units" from to_ amount)
    | Error err ->
        printfn "Optimization failed: %s" err.Message
}
```

---

## HybridSolver

**Module:** `FSharp.Azure.Quantum.HybridSolver`

Routes a problem to a classical heuristic or to a quantum solver. `QuantumAdvisor` recommends by problem size (`QuantumAdvisor.defaultThresholds`: classical below 20, "consider quantum" from 20, "strongly quantum" from 50). HybridSolver runs quantum only when the advisor strongly recommends it **and** a backend was passed (the `...WithBackendAsync` functions), unless you force a method.

```text
val solveTspAsync     : distances:float[,] → budget:float option → timeout:float option → forceMethod:SolverMethod option
                        → cancellationToken:CancellationToken → Task<QuantumResult<Solution<TspSolver.TspSolution>>>
val solvePortfolioAsync : assets:PortfolioSolver.Asset list → constraints:PortfolioSolver.Constraints
                        → budget → timeout → forceMethod → cancellationToken
                        → Task<QuantumResult<Solution<PortfolioSolver.PortfolioSolution>>>
val solvePortfolioWithCovarianceAsync : assets:PortfolioSolver.Asset list → covariance:float[,] → constraints
                        → budget → timeout → forceMethod → backend:IQuantumBackend option → cancellationToken
                        → Task<QuantumResult<Solution<PortfolioSolver.PortfolioSolution>>>
val solveMaxCutAsync  : QuantumMaxCutSolver.MaxCutProblem → budget → timeout → forceMethod → cancellationToken
                        → Task<QuantumResult<Solution<QuantumMaxCutSolver.MaxCutSolution>>>
val solveKnapsackAsync : QuantumKnapsackSolver.KnapsackProblem → budget → timeout → forceMethod → cancellationToken
                        → Task<QuantumResult<Solution<QuantumKnapsackSolver.KnapsackSolution>>>
val solveGraphColoringAsync : QuantumGraphColoringSolver.GraphColoringProblem → numColors:int → budget → timeout
                        → forceMethod → cancellationToken
                        → Task<QuantumResult<Solution<QuantumGraphColoringSolver.GraphColoringSolution>>>

// Each has a ...WithBackendAsync variant taking an IQuantumBackend option before the token;
// solveTspWithBackendAndConfigAsync also takes a QuantumTspSolver.QuantumTspConfig.
// solvePortfolioWithCovarianceAsync validates the covariance and passes it to both paths, which
// then report risk as sqrt(w'Σw); the classical path still picks assets by return/risk ratio.
// solvePortfolioAsync treats the assets as independent.

type SolverMethod = Classical | Quantum

type Solution<'TResult> = {
    Method: SolverMethod
    Result: 'TResult
    Reasoning: string
    ElapsedMs: float
    Recommendation: QuantumAdvisor.Recommendation option
}
```

- `budget` (USD): if the estimated quantum cost exceeds it, the classical solver runs instead.
- `timeout`: accepted but not currently used.
- `forceMethod`: `Some HybridSolver.Classical` or `Some HybridSolver.Quantum` bypasses the advisor (forced quantum uses the given backend or a new LocalBackend).

The MaxCut, Knapsack and Graph Coloring variants take the solver-level problem types from `FSharp.Azure.Quantum.Quantum`, not the builder types above; see the [FAQ](faq#when-should-i-use-quantum-vs-hybridsolver) for a conversion.

---

## Quantum Backends

**Modules:** `FSharp.Azure.Quantum.Core.BackendAbstraction` (the `IQuantumBackend` interface), `FSharp.Azure.Quantum.Backends` (implementations)

### LocalBackend

**Characteristics:**
- ✅ Free (local state-vector simulation)
- ✅ Fast for small circuits (milliseconds per gate up to about 20 qubits)
- ✅ Up to `StateVector.maxQubits` (derived from available memory; hard ceiling 30; override with the `FSAQ_MAX_QUBITS` environment variable)
- ✅ Iterative algorithms budget against `StateVector.practicalCircuitQubits` (default 20; `FSAQ_MAX_CIRCUIT_QUBITS`)

```fsharp
open FSharp.Azure.Quantum.Backends

let backend = LocalBackendFactory.createUnified()   // or: LocalBackend.LocalBackend() :> IQuantumBackend

// Use with any solver
task {
    match! GraphColoring.solveAsync problem 3 (Some backend) CancellationToken.None with
    | Ok solution -> printfn "Colors used: %d" solution.ColorsUsed
    | Error err -> printfn "Error: %s" err.Message
}
```

### Cloud Backends (via CloudBackendFactory)

**Module:** `FSharp.Azure.Quantum.Backends.CloudBackends`

Create cloud backends for different quantum hardware providers. All cloud backends implement `IQuantumBackend` (both sync and async) and `IQubitLimitedBackend`. They need an `HttpClient` that authenticates to Azure Quantum and the workspace URL.

```fsharp
open FSharp.Azure.Quantum.Backends.CloudBackends

// Reuses httpClient and workspaceUrl from Pattern 2 above
let rigetti    = CloudBackendFactory.createRigetti httpClient workspaceUrl "rigetti.sim.qvm" 1000
let ionq       = CloudBackendFactory.createIonQ httpClient workspaceUrl "ionq.simulator" 1000
let quantinuum = CloudBackendFactory.createQuantinuum httpClient workspaceUrl "quantinuum.sim.h1-1sc" 1000
let atom       = CloudBackendFactory.createAtomComputing httpClient workspaceUrl "atom-computing.sim" 1000
let iqm        = CloudBackendFactory.createIqm httpClient workspaceUrl "iqm.sim" 1000

task {
    match! GraphColoring.solveAsync problem 3 (Some ionq) CancellationToken.None with
    | Ok solution -> printfn "Executed on: %s" solution.BackendName
    | Error err -> printfn "Error: %s" err.Message
}
```

```text
val CloudBackendFactory.createRigetti       : httpClient:HttpClient -> workspaceUrl:string -> target:string -> shots:int -> IQuantumBackend
val CloudBackendFactory.createRigettiRouted : httpClient:HttpClient -> workspaceUrl:string -> target:string -> shots:int -> couplingMap:QubitRouting.CouplingMap -> IQuantumBackend
val CloudBackendFactory.createIonQ          : httpClient:HttpClient -> workspaceUrl:string -> target:string -> shots:int -> IQuantumBackend
val CloudBackendFactory.createQuantinuum    : httpClient:HttpClient -> workspaceUrl:string -> target:string -> shots:int -> IQuantumBackend
val CloudBackendFactory.createAtomComputing : httpClient:HttpClient -> workspaceUrl:string -> target:string -> shots:int -> IQuantumBackend
val CloudBackendFactory.createIqm           : httpClient:HttpClient -> workspaceUrl:string -> target:string -> shots:int -> IQuantumBackend
```

Qubit limits the cloud backends report (`MaxQubits`, from the target name): IonQ Aria 25 / Forte 36, Rigetti QPU 84, Quantinuum H1 32 / H2 56, Atom Computing QPU 100, IQM 20; provider simulator targets 20.

**Async usage with cloud backends:**

```fsharp
open System
open System.Threading
open FSharp.Azure.Quantum.Core

let bell =
    CircuitBuilder.empty 2
    |> CircuitBuilder.addGate (CircuitBuilder.Gate.H 0)
    |> CircuitBuilder.addGate (CircuitBuilder.Gate.CNOT(0, 1))
    |> CircuitAbstraction.wrapCircuit

let cts = new CancellationTokenSource(TimeSpan.FromSeconds(60.0))

// Async execution (recommended for cloud - avoids blocking during network I/O)
let run =
    task {
        let! result = ionq.ExecuteToStateAsync bell cts.Token
        match result with
        | Ok state -> printfn "Executed on: %s" ionq.Name
        | Error err -> printfn "Error: %s" err.Message
    }
```

> **Note:** Cloud backends' `ApplyOperationAsync` always returns `Error` because cloud providers do not support incremental state operations. Use `ExecuteToStateAsync` for full circuit execution. The library's solvers and algorithms do this themselves: on a cloud backend they record their gates and submit the complete circuit (`UnifiedBackend.submitAsCircuit`), and the backend transpiles it to the provider's native gates before conversion.

**Job budget:** every `ExecuteToState` is one billed job. The backend types take an optional `jobBudget` (`CloudBackendHelpers.JobBudget`; `JobBudget.Limit n` allows n jobs, `JobBudget()` counts without a limit), and the job after the limit is refused with a `QuotaExceeded` error before submission. One budget can be shared by several backends; `IJobCountingBackend.JobBudget` exposes it, with `Submitted`, `MaxJobs` and `Remaining`.

```fsharp
open FSharp.Azure.Quantum.Backends

let budget = CloudBackendHelpers.JobBudget.Limit 500
let budgetedIonQ = CloudBackends.IonQCloudBackend(httpClient, workspaceUrl, "ionq.simulator", 1000, jobBudget = budget)

task {
    match! GraphColoring.solveAsync problem 3 (Some(budgetedIonQ :> BackendAbstraction.IQuantumBackend)) CancellationToken.None with
    | Ok solution -> printfn "Done after %d jobs" budget.Submitted
    | Error err -> printfn "Error (after %d jobs): %s" budget.Submitted err.Message
}
```

> **Result format:** cloud results are measurement histograms, and the returned `QuantumState` is reconstructed from them in tiers by circuit width: a dense state vector up to `StateVector.maxQubits`, a `SparseState` (observed outcomes only) from there through 31 qubits, and `QuantumState.MeasurementHistogram` (bitstring → count, at most `shots` entries) above that. The histogram tier has no width limit, so wide devices such as Quantinuum H2 (56 qubits) and IonQ Forte (36 qubits) are usable.

For D-Wave annealers see `FSharp.Azure.Quantum.Backends.DWaveBackend` (a local mock) and `RealDWaveBackend`, and the [Backend Switching](backend-switching) guide.

### Backend Selection Guide

| Problem Size | Recommended Backend | Rationale |
|--------------|---------------------|-----------|
| Up to ~20 qubits | LocalBackend | Free, fast enough for iterative algorithms |
| Up to `StateVector.maxQubits` | LocalBackend (single circuits) | Fits in memory, but each extra qubit doubles the time per gate |
| Beyond the local limit | IonQ/Rigetti/Quantinuum/Atom Computing/IQM QPU | Provider simulators are capped at 20 qubits by the library |

### IQubitLimitedBackend Interface

**Module:** `FSharp.Azure.Quantum.Core.BackendAbstraction`

Optional interface for backends that report qubit capacity limits. Solvers can test for this interface to query capacity without requiring all backends to implement it.

```text
/// Inherits IQuantumBackend, adds qubit limit reporting.
type IQubitLimitedBackend =
    inherit IQuantumBackend
    /// Maximum number of qubits supported (None = unlimited/unknown).
    abstract member MaxQubits: int option
```

**Convenience wrapper:**

```text
val UnifiedBackend.getMaxQubits : backend:IQuantumBackend → int option
```

Returns `Some limit` if the backend implements `IQubitLimitedBackend`, otherwise `None`.

```fsharp
open FSharp.Azure.Quantum.Core.BackendAbstraction

let backend = LocalBackendFactory.createUnified()

// Check backend capacity
match UnifiedBackend.getMaxQubits backend with
| Some limit -> printfn "Backend supports up to %d qubits" limit
| None -> printfn "Backend has no known qubit limit"

// Pattern-match directly on the interface
match backend with
| :? IQubitLimitedBackend as lb ->
    printfn "Max qubits: %A" lb.MaxQubits
| _ ->
    printfn "Backend does not report qubit limits"
```

---

## C# Interop

**Class:** `FSharp.Azure.Quantum.CSharpBuilders` (static methods taking arrays of value tuples)

The main problem builders have C#-friendly static methods. F# `option` parameters take `null` for `None`, and the `...Async` solvers return a `Task` of an `FSharpResult` value with `IsOk`, `ResultValue` and `ErrorValue`:

```csharp
using System.Threading;
using FSharp.Azure.Quantum;
using static FSharp.Azure.Quantum.CSharpBuilders;

// MaxCut
var vertices = new[] { "A", "B", "C" };
var edges = new[] {
    (source: "A", target: "B", weight: 1.0),
    (source: "B", target: "C", weight: 2.0)
};
var maxCutProblem = MaxCutProblem(vertices, edges);
var result = await MaxCut.solveAsync(maxCutProblem, null, CancellationToken.None);
if (result.IsOk) Console.WriteLine(result.ResultValue.CutValue);
else Console.WriteLine(result.ErrorValue.Message);

// Knapsack
var items = new[] {
    (id: "laptop", weight: 3.0, value: 1000.0)
};
var knapsackProblem = KnapsackProblem(items, capacity: 5.0);

// TSP
var cities = new[] {
    (name: "Seattle", x: 0.0, y: 0.0)
};
var tspProblem = TspProblem(cities);

// Portfolio
var assets = new[] {
    (symbol: "AAPL", expectedReturn: 0.12, risk: 0.15, price: 150.0)
};
var portfolioProblem = PortfolioProblem(assets, budget: 10000.0);
var correlatedProblem = PortfolioProblem(assets, budget: 10000.0, covariance: new double[,] { { 0.0225 } });
```

`CSharpBuilders` also has entry points for the business and advanced builders (`CoverageProblemAsync`, `PairingProblemAsync`, `PackingProblemAsync`, `FactorInteger`, `SolveTreeSearch`, `PriceEuropeanCallAsync`, ...), and `QuantumBackendCSharpExtensions` adds Task-returning helpers such as `backend.ExecuteToStateTask(circuit)`. All live in `Builders/BuildersCSharpExtensions.fs`. See `examples/CSharpConsumer` for a complete C# project.

---

## Core Types

### Result Type

The solvers return `Task<QuantumResult<'T>>` (`QuantumResult<'T>` = `Result<'T, QuantumError>`; see [Error Handling](#error-handling)):

```fsharp
task {
    match! MaxCut.solveAsync problem_maxcut None CancellationToken.None with
    | Ok solution -> 
        // Success case
        printfn "Solution: %A" solution
    | Error err -> 
        // Failure case: a QuantumError, not a string
        printfn "Error: %s" err.Message
}
```

### IQuantumBackend Interface

**Module:** `FSharp.Azure.Quantum.Core.BackendAbstraction`

```text
type IQuantumBackend =
    /// Execute circuit and return quantum state
    abstract member ExecuteToState: ICircuit -> Result<QuantumState, QuantumError>
    /// Backend's native state representation type
    abstract member NativeStateType: QuantumStateType
    /// Apply quantum operation to existing state
    abstract member ApplyOperation: QuantumOperation -> QuantumState -> Result<QuantumState, QuantumError>
    /// Check if backend supports a specific operation type
    abstract member SupportsOperation: QuantumOperation -> bool
    /// Backend name (for logging and diagnostics)
    abstract member Name: string
    /// Initialize quantum state without running a circuit
    abstract member InitializeState: int -> Result<QuantumState, QuantumError>
    
    // Async variants (Task-based, with CancellationToken)
    /// Execute circuit asynchronously
    abstract member ExecuteToStateAsync: ICircuit -> CancellationToken -> Task<Result<QuantumState, QuantumError>>
    /// Apply quantum operation asynchronously
    abstract member ApplyOperationAsync: QuantumOperation -> QuantumState -> CancellationToken -> Task<Result<QuantumState, QuantumError>>
```

> **Note:** Async methods use `System.Threading.Tasks.Task<T>` (not F# `Async<T>`), with `CancellationToken` as the last parameter. Use the `task { }` computation expression when calling these methods.

See also `IQubitLimitedBackend` (inherits `IQuantumBackend`, adds `MaxQubits: int option`) and
`IWallClockLimitedBackend` (adds `PracticalQubits: int`) in the [IQubitLimitedBackend Interface](#iqubitlimitedbackend-interface) section above.

### UnifiedBackend Module

**Module:** `FSharp.Azure.Quantum.Core.BackendAbstraction`

Higher-level helpers for applying operations through any `IQuantumBackend`.

```text
// Sync
val UnifiedBackend.getCapabilities : backend:IQuantumBackend -> BackendCapabilities
val UnifiedBackend.getMaxQubits : backend:IQuantumBackend -> int option
val UnifiedBackend.getRunnableQubits : backend:IQuantumBackend -> int option   // min of capacity and wall-clock limit
val UnifiedBackend.applyWithConversion : backend:IQuantumBackend -> operation:QuantumOperation -> state:QuantumState -> Result<QuantumState, QuantumError>
val UnifiedBackend.applySequence : backend:IQuantumBackend -> operations:QuantumOperation list -> initialState:QuantumState -> Result<QuantumState, QuantumError>

// Async
val UnifiedBackend.applyWithConversionAsync : backend:IQuantumBackend -> operation:QuantumOperation -> state:QuantumState -> ct:CancellationToken -> Task<Result<QuantumState, QuantumError>>
val UnifiedBackend.applySequenceAsync : backend:IQuantumBackend -> operations:QuantumOperation list -> initialState:QuantumState -> ct:CancellationToken -> Task<Result<QuantumState, QuantumError>>
```

### Circuit Types

General circuits (`FSharp.Azure.Quantum.CircuitBuilder`):

```text
type Circuit = { QubitCount: int; Gates: Gate list }

type Gate =
    | X of int | Y of int | Z of int | H of int
    | S of int | SDG of int | T of int | TDG of int
    | P of int * float | RX of int * float | RY of int * float | RZ of int * float
    | U3 of int * float * float * float
    | CNOT of int * int | CZ of int * int | CP of int * int * float
    | CRX of int * int * float | CRY of int * int * float | CRZ of int * int * float
    | SWAP of int * int | RXX of int * int * float | RYY of int * int * float | RZZ of int * int * float
    | CCX of int * int * int | MCZ of controls: int list * target: int
    | Measure of int | Reset of int | Barrier of int list
    | Conditional of measuredQubit: int * gate: Gate
```

Build with `CircuitBuilder.empty n |> CircuitBuilder.addGate ...` or the `circuit { ... }` computation expression, and pass to a backend as `CircuitAbstraction.wrapCircuit c` (an `ICircuit`).

QAOA circuits (`FSharp.Azure.Quantum.Core.QaoaCircuit`):

```text
type QuantumGate =
    | H of qubit: int
    | RX of qubit: int * angle: float
    | RY of qubit: int * angle: float
    | RZ of qubit: int * angle: float
    | RZZ of qubit1: int * qubit2: int * angle: float
    | CNOT of control: int * target: int

type QaoaLayer = {
    CostGates: QuantumGate[]
    MixerGates: QuantumGate[]
    Gamma: float
    Beta: float
}

type QaoaCircuit = {
    NumQubits: int
    InitialStateGates: QuantumGate[]
    Layers: QaoaLayer[]
    ProblemHamiltonian: ProblemHamiltonian
    MixerHamiltonian: MixerHamiltonian
}
```

---

## Quantum Linear System Solver (HHL Algorithm)

**Module:** `FSharp.Azure.Quantum.QuantumLinearSystemSolver`

**Use Cases:** learning and experimenting with HHL on small systems (2×2 to 16×16), e.g. the linear-algebra step of least-squares regression.

**Algorithm:** HHL (Harrow-Hassidim-Lloyd) - prepares a quantum state proportional to the solution of Ax = b

### What is HHL?

HHL solves linear systems **Ax = b** where:
- **Input**: Hermitian matrix A (N×N), vector |b⟩
- **Output**: Quantum state |x⟩ encoding the solution (not a classical vector)
- **Theory**: O(log(N) × poly(κ, 1/ε)) for sparse, well-conditioned A, versus O(N³) for Gaussian elimination. The advantage only holds when A can be loaded efficiently and you need a property of |x⟩ rather than all of its entries.

This library runs HHL on small matrices, on the local simulator or as one whole-circuit job on a cloud backend; it does not run faster than a classical solver at these sizes.

### Computation Expression API

The builder validates the problem and returns `QuantumResult<LinearSystemProblem>`, so bind it before solving:

```fsharp
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.QuantumLinearSystemSolver

// Simple 2×2 system: [[3,1],[1,3]] * x = [1,0]
let hhlProblem = linearSystemSolver {
    matrix [[3.0; 1.0]; [1.0; 3.0]]
    vector [1.0; 0.0]
    precision 4  // 4 eigenvalue qubits = 16 bins
}

match hhlProblem |> Result.bind solve with
| Ok solution ->
    printfn "Success probability: %.4f" solution.SuccessProbability
    printfn "Condition number: %A" solution.ConditionNumber
    printfn "Gates used: %d" solution.GateCount
| Error err ->
    printfn "Error: %s" err.Message
```

### Advanced Configuration

```fsharp
// ExactRotation, LinearApproximation and PiecewiseLinear live in HHLTypes
open FSharp.Azure.Quantum.Algorithms.HHLTypes

// Diagonal system (faster, more accurate)
let diagonalProblem = linearSystemSolver {
    diagonalMatrix [2.0; 4.0; 8.0; 16.0]  // Eigenvalues
    vector [1.0; 1.0; 1.0; 1.0]
    eigenvalueQubits 6                     // Same as precision
    inversionMethod (ExactRotation 1.0)   // Exact vs linear approximation
    minEigenvalue 0.001                    // Stability threshold
    postSelection true                     // Higher accuracy, lower success rate
    backend (LocalBackendFactory.createUnified())   // Any IQuantumBackend (default: LocalBackend)
}
```

The `shots` operation is accepted but not used. On a simulator the solver reads the solution and success probability from the state vector exactly; on a cloud backend they come from the shots that backend was created with.

**Limits** (checked when the builder finishes): matrix dimension 2–16 and a power of 2; `eigenvalueQubits` 2–12; eigenvalue + solution + ancilla qubits ≤ 20.

### Types

```text
// HermitianMatrix, QuantumVector and EigenvalueInversionMethod come from
// FSharp.Azure.Quantum.Algorithms.HHLTypes
type LinearSystemProblem = {
    Matrix: HermitianMatrix
    InputVector: QuantumVector
    EigenvalueQubits: int
    InversionMethod: EigenvalueInversionMethod
    MinEigenvalue: float
    UsePostSelection: bool
    Backend: IQuantumBackend option
    Shots: int option
}

type EigenvalueInversionMethod =
    | ExactRotation of normalizationConstant: float
    | LinearApproximation of normalizationConstant: float
    | PiecewiseLinear of segments: (float * float * float)[]

type LinearSystemSolution = {
    SuccessProbability: float
    EstimatedEigenvalues: float[]
    ConditionNumber: float option
    GateCount: int
    PostSelectionSuccess: bool
    SolutionAmplitudes: Map<int, Complex> option
    Readout: HhlReadout        // Amplitudes | MeasuredMagnitudes | MeasuredRelativePhases
    BackendName: string
    IsQuantum: bool
    Success: bool
    Message: string
}
```

### Functions

```text
val linearSystemSolver : LinearSystemSolverBuilder   // Run returns QuantumResult<LinearSystemProblem>
val solve : LinearSystemProblem → QuantumResult<LinearSystemSolution>
val solve2x2 : a11:float → a12:float → a21:float → a22:float → b1:float → b2:float → QuantumResult<LinearSystemSolution>
val solveDiagonal : eigenvalues:float list → inputVector:float list → QuantumResult<LinearSystemSolution>
```

Builder operations: `matrix`, `diagonalMatrix`, `vector`, `eigenvalueQubits` (alias `precision`), `inversionMethod`, `minEigenvalue`, `postSelection`, `backend`, `shots` (ignored).

### Example: Engineering Simulation

```fsharp
// Solve heat equation discretization: Ax = b
// A = tridiagonal matrix (heat diffusion operator)
// b = boundary conditions

let heatDiffusion = linearSystemSolver {
    matrix [
        [ 2.0; -1.0;  0.0;  0.0]
        [-1.0;  2.0; -1.0;  0.0]
        [ 0.0; -1.0;  2.0; -1.0]
        [ 0.0;  0.0; -1.0;  2.0]
    ]
    vector [100.0; 0.0; 0.0; 50.0]  // Boundary temps (normalized to |b⟩)
    precision 6
    minEigenvalue 0.01  // Avoid small eigenvalues
}

match heatDiffusion |> Result.bind solve with
| Ok solution ->
    printfn "Temperature distribution computed (up to normalization)"
    printfn "Condition number: %.2f" (defaultArg solution.ConditionNumber 0.0)
    
    match solution.SolutionAmplitudes with
    | Some amplitudes ->
        amplitudes 
        |> Map.iter (fun idx amp -> 
            printfn "  Point %d: %.4f" idx amp.Magnitude)
    | None ->
        printfn "No amplitudes available from this backend"
| Error err ->
    printfn "Simulation failed: %s" err.Message
```

### Example: Machine Learning (Least Squares)

```fsharp
// Solve normal equations: (X^T X) w = X^T y
// For linear regression: find weights w

let leastSquares = linearSystemSolver {
    // X^T X for two features (symmetric positive definite; dimension must be a power of 2)
    matrix [
        [10.0;  5.0]
        [ 5.0; 12.0]
    ]
    // Right-hand side X^T y
    vector [15.0; 20.0]
    precision 8
    postSelection true
}

match leastSquares |> Result.bind solve with
| Ok solution ->
    printfn "Weights found (as a normalized quantum state)"
    printfn "Success rate: %.2f%%" (solution.SuccessProbability * 100.0)
| Error err ->
    printfn "Training failed: %s" err.Message
```

For a higher-level regression workflow (training config, intercept fitting, and metrics), see `FSharp.Azure.Quantum.MachineLearning.QuantumRegressionHHL` and `examples/LinearSystemSolver/QuantumRegressionHHLExample.fsx`.

```fsharp
open FSharp.Azure.Quantum.MachineLearning

let regressionConfig : QuantumRegressionHHL.RegressionConfig = {
    TrainX = [| [| 1.0 |]; [| 2.0 |]; [| 3.0 |] |]
    TrainY = [| 3.0; 5.0; 7.0 |]
    EigenvalueQubits = 4
    MinEigenvalue = 0.01
    Backend = LocalBackendFactory.createUnified()
    Shots = 2000
    FitIntercept = true
    Verbose = false
    Logger = None
}

match QuantumRegressionHHL.train regressionConfig with
| Ok result -> printfn "Weights: %A" result.Weights
| Error err -> printfn "Training failed: %s" err.Message
```

### Important Limitations

**Implementation Notes (This Library):**
- Diagonal matrices use a simpler, more accurate shortcut.
- General Hermitian matrices are lowered into an explicit gate sequence using controlled Trotter-Suzuki Hamiltonian evolution; when targeting gate-based hardware backends, the planned circuit is transpiled to the backend gate set during planning.

**Matrix Requirements:**
- Must be **Hermitian** (A = A†) - real symmetric matrices qualify
- Non-Hermitian can be embedded: [[0, A], [A†, 0]]
- Dimension must be power of 2 (2×2, 4×4, 8×8, 16×16)

**Solution Format:**
- Output is a **quantum state |x⟩** (normalized), not a classical vector
- Local simulation: `SolutionAmplitudes` holds the amplitudes, signs and phases included (`Readout = Amplitudes`)
- Cloud backends: the state preparation of |b⟩ and the HHL circuit run as one whole-circuit job, and `SolutionAmplitudes` holds the magnitudes |xᵢ| from the measured counts, post-selected on ancilla = 1 with the eigenvalue register at 0 (`Readout = MeasuredMagnitudes`). Counts carry no signs or phases. Every amplitude from counts needs a number of shots that grows with the dimension
- `HHL.executeWithRelativePhases config backend` also measures the signs and relative phases on a cloud backend (`Readout = MeasuredRelativePhases`): besides the magnitude circuit it runs, per solution qubit q, the same HHL circuit with a Hadamard on q before measurement. The post-selected outcomes i and i + 2^q then differ by 2·Re(x̄ᵢ·xᵢ₊₂^q), so every pair of components differing in one bit gets its relative sign, and the signs are chained from the largest component along the best-conditioned pairs. A real system (real A and b, as in least squares) needs 1 + log₂N jobs; a complex one adds an RX(π/2) circuit per qubit for the imaginary parts (1 + 2·log₂N). On a simulator it is `HHL.execute` and submits nothing (`Circuits = 0`)
- `QuantumRegressionHHL` uses `executeWithRelativePhases`, so it fits signed weights on a cloud backend too: 1 + log₂(padded dimension) jobs per fit (`RegressionResult.Circuits`; 3 for 4 weights), with the overall scale set by the same least-squares fit as on the simulator

**Performance Considerations:**
- **Condition number κ**: Lower is better (κ < 100 recommended)
- **Success probability**: ∝ 1/κ² (ill-conditioned = low success rate)
- **Size**: the library accepts matrices up to 16×16, which classical solvers handle instantly

### When to Use HHL vs Classical

For solving linear systems in production, use a classical solver: at the sizes this library can simulate, Gaussian elimination is faster and exact. Use the HHL builder to learn and experiment with the algorithm, to study success probability versus condition number, or as a building block (`QuantumRegressionHHL`).

---

## QAOA Execution Helpers

**Module:** `FSharp.Azure.Quantum.Core.QaoaExecutionHelpers`

Shared QAOA execution infrastructure for all quantum solvers. Consolidates QAOA circuit construction, parameter optimization, and measurement into reusable functions. Supports both dense (`float[,]`) and sparse (`Map<int * int, float>`) QUBO representations, and provides budget-constrained execution with backend capacity checking.

### Configuration Types

```text
/// Unified QAOA execution configuration.
type QaoaSolverConfig = {
    NumLayers: int                   // QAOA layers (p parameter)
    OptimizationShots: int           // Shots per optimization iteration
    FinalShots: int                  // Shots for final measurement
    EnableOptimization: bool         // Enable Nelder-Mead (false = grid search)
    EnableConstraintRepair: bool     // Enable constraint repair post-processing
    MaxOptimizationIterations: int   // Max Nelder-Mead iterations
}
```

### Preset Configurations

```text
val defaultConfig     : QaoaSolverConfig   // Balanced (2 layers, 100/1000 shots, optimization on, 1000 iterations)
val fastConfig        : QaoaSolverConfig   // Quick prototyping (1 layer, 50/500 shots, grid search)
val highQualityConfig : QaoaSolverConfig   // Larger budget (3 layers, 200/2000 shots, optimization on, 1000 iterations)
```

### Dense QUBO Functions

Every execution entry point is Task-based and takes a `CancellationToken`; there are no blocking variants.

```text
val evaluateQubo :
    qubo:float[,] → bits:int[] → float
```

**Execution functions** (Task-based, with `CancellationToken` and `maxConcurrency` for grid search):

```text
val executeQaoaCircuitAsync :
    backend:IQuantumBackend → problemHam:ProblemHamiltonian → mixerHam:MixerHamiltonian
    → parameters:(float * float)[] → shots:int → cancellationToken:CancellationToken
    → Task<Result<int[][], QuantumError>>

val executeQaoaWithOptimizationAsync :
    backend:IQuantumBackend → qubo:float[,] → config:QaoaSolverConfig
    → cancellationToken:CancellationToken
    → Task<Result<int[] * (float * float)[] * bool, QuantumError>>

val executeQaoaWithGridSearchAsync :
    backend:IQuantumBackend → qubo:float[,] → config:QaoaSolverConfig
    → maxConcurrency:int → cancellationToken:CancellationToken
    → Task<Result<int[] * (float * float)[], QuantumError>>

val executeFromQuboAsync :
    backend:IQuantumBackend → qubo:float[,] → parameters:(float * float)[] → shots:int
    → cancellationToken:CancellationToken → Task<Result<int[][], QuantumError>>
```

**Parameters:**
- `qubo` — Dense QUBO matrix (`float[,]`)
- `config` — QAOA solver configuration
- `backend` — Quantum backend (always passed explicitly)

**Angle conventions:**
- Every circuit is built from the cost Hamiltonian scaled to a largest |coefficient| of 1 (`ProblemHamiltonian.normalize`), so a (γ, β) pair means the same for a unit-weight MaxCut and for a penalty QUBO with coefficients in the thousands.
- The mixer is `-Σ Xᵢ` (`MixerHamiltonian.create`), so small γ > 0 with 0 < β < π/4 lowers the expected QUBO energy (minimisation).
- Grid search and Nelder-Mead rank angles by the expected QUBO energy: exact from the amplitudes when the backend returns a state vector, otherwise the mean over `OptimizationShots` samples. Nelder-Mead starts from a ramp (γ rising, β falling across the layers).

### Sparse QUBO Functions

Memory-efficient path that avoids allocating dense `float[,]` arrays. Preferred for large, sparse QUBO problems.

```text
val evaluateQuboSparse :
    quboMap:Map<int * int, float> → bits:int[] → float
```

**Execution functions:**

```text
val executeQaoaCircuitSparseAsync :
    backend:IQuantumBackend → numQubits:int → quboMap:Map<int * int, float>
    → parameters:(float * float)[] → shots:int → cancellationToken:CancellationToken
    → Task<Result<int[][], QuantumError>>

val executeQaoaWithOptimizationSparseAsync :
    backend:IQuantumBackend → numQubits:int → quboMap:Map<int * int, float>
    → config:QaoaSolverConfig → cancellationToken:CancellationToken
    → Task<Result<int[] * (float * float)[] * bool, QuantumError>>

val executeQaoaWithGridSearchSparseAsync :
    backend:IQuantumBackend → numQubits:int → quboMap:Map<int * int, float>
    → config:QaoaSolverConfig → maxConcurrency:int → cancellationToken:CancellationToken
    → Task<Result<int[] * (float * float)[], QuantumError>>
```

**Parameters:**
- `numQubits` — Number of qubits (variables) in the QUBO
- `quboMap` — Sparse QUBO as `Map<(i, j), coefficient>` (only non-zero entries)

### Budget Execution Types

```text
/// Capacity-check strategy for budget-constrained execution.
type BudgetDecompositionStrategy =
    | NoBudgetDecomposition             // No capacity check
    | FixedQubitLimit of maxQubits: int  // Error if problem exceeds limit
    | AdaptiveToBudgetBackend           // Use backend's MaxQubits

/// Budget constraints for QAOA execution.
type ExecutionBudget = {
    MaxTotalShots: int                  // Max shots across all sub-problems
    MaxTimeMs: int option               // Optional wall-clock limit (ms)
    Decomposition: BudgetDecompositionStrategy
}
```

### Budget Execution Functions

```text
val defaultBudget : ExecutionBudget
    // 1000 shots, no time limit, AdaptiveToBudgetBackend
```

**Execution function:**

```text
val executeWithBudgetAsync :
    backend:IQuantumBackend → qubo:float[,] → config:QaoaSolverConfig
    → budget:ExecutionBudget → maxConcurrency:int → cancellationToken:CancellationToken
    → Task<Result<int[] * (float * float)[] * bool, QuantumError>>
```

### Example: Sparse QUBO Execution

```fsharp
open System.Threading
open FSharp.Azure.Quantum.Core.QaoaExecutionHelpers

let backend = LocalBackendFactory.createUnified()

// Define a sparse QUBO (only non-zero entries)
let quboMap =
    Map.ofList [
        (0, 0), -1.0
        (1, 1), -1.0
        (0, 1),  2.0
    ]

task {
    match! executeQaoaWithOptimizationSparseAsync backend 2 quboMap defaultConfig CancellationToken.None with
    | Ok (bestBits, parameters, converged) ->
        let energy = evaluateQuboSparse quboMap bestBits
        printfn "Best bitstring: %A" bestBits
        printfn "Energy: %.4f" energy
        printfn "Converged: %b" converged
    | Error err ->
        printfn "Error: %s" err.Message
}
```

### Example: Budget-Constrained Execution

```fsharp
open System.Threading
open FSharp.Azure.Quantum.Core.QaoaExecutionHelpers

let qubo = Array2D.init 4 4 (fun i j -> if i = j then -1.0 elif abs (i - j) = 1 then 0.5 else 0.0)

let budget = {
    MaxTotalShots = 500
    MaxTimeMs = Some 5000       // 5-second wall-clock limit
    Decomposition = AdaptiveToBudgetBackend
}

let budgetResult =
    // maxConcurrency = 1
    executeWithBudgetAsync backend qubo defaultConfig budget 1 CancellationToken.None
    |> Async.AwaitTask
    |> Async.RunSynchronously

match budgetResult with
| Ok (bits, parameters, converged) ->
    printfn "Solution: %A (converged=%b)" bits converged
| Error err ->
    printfn "Budget execution failed: %s" err.Message
```

---

## Problem Decomposition

**Module:** `FSharp.Azure.Quantum.Core.ProblemDecomposition`

Generic problem decomposition orchestrator for QAOA solvers. When a problem requires more qubits than the backend supports (`IQubitLimitedBackend.MaxQubits`), automatically splits the problem into sub-problems, solves them independently, and recombines the results. Fully generic over problem and solution types — solvers supply decompose/recombine/solve functions.

It is used by the vertex cover, clique, set cover, SAT, matching, bin packing and binary ILP solvers (and so by the business builders on top of them). The MaxCut, Knapsack, TSP, Portfolio, Graph Coloring and Network Flow solvers do not decompose: they return an error when the problem is too wide for the backend.

### Strategy Types

```text
/// Strategy for decomposing a problem when it exceeds backend capacity.
type DecompositionStrategy =
    | NoDecomposition                              // Run as-is
    | FixedPartition of maxQubitsPerPartition: int // Fixed-size partitions
    | AdaptiveToBackend                            // Auto from backend MaxQubits

/// Result of the decomposition planning step.
type DecompositionPlan<'Problem> =
    | RunDirect of 'Problem            // Fits within capacity
    | RunDecomposed of 'Problem list   // Split into sub-problems
```

### Planning and Execution Functions

```text
val plan :
    strategy:DecompositionStrategy → backend:IQuantumBackend
    → estimateQubits:('Problem → int) → decomposeFn:('Problem → 'Problem list)
    → problem:'Problem → DecompositionPlan<'Problem>

val execute :
    solveFn:('Problem → Result<'Solution, QuantumError>)
    → recombineFn:('Solution list → 'Solution)
    → plan:DecompositionPlan<'Problem> → Result<'Solution, QuantumError>

val solveWithDecomposition :
    backend:IQuantumBackend → problem:'Problem
    → estimateQubits:('Problem → int) → decomposeFn:('Problem → 'Problem list)
    → recombineFn:('Solution list → 'Solution)
    → solveFn:('Problem → Result<'Solution, QuantumError>)
    → Result<'Solution, QuantumError>
```

**Parameters:**
- `estimateQubits` — Function to estimate qubit count for a problem
- `decomposeFn` — Function to split a problem into sub-problems
- `recombineFn` — Function to merge sub-solutions into one
- `solveFn` — Function to solve a single (sub-)problem

### Graph Decomposition Helpers

Utility functions for graph-based solvers to decompose problems by connected components using union-find.

```text
val connectedComponents :
    numVertices:int → edges:(int * int) list → int list list

val partitionByComponents :
    numVertices:int → edges:(int * int) list → (int list * (int * int) list) list

val canDecomposeWithinLimit :
    numVertices:int → edges:(int * int) list → maxQubitsPerPart:int
    → qubitsPerVertex:int → bool
```

**Parameters:**
- `numVertices` — Total number of vertices (0-indexed)
- `edges` — Undirected edges as `(int * int)` pairs
- `maxQubitsPerPart` — Maximum qubits per sub-problem
- `qubitsPerVertex` — Qubits per vertex (typically 1 for MaxCut, numColors for coloring)

### Example: Solver Integration

```fsharp
open FSharp.Azure.Quantum.Core.ProblemDecomposition

// A graph problem: vertex count + edges (0-based indices)
type GraphProblem = { NumVertices: int; GraphEdges: (int * int) list }

// Solver-supplied functions
let estimateQubits (p: GraphProblem) = p.NumVertices   // one qubit per vertex
let decompose (p: GraphProblem) =
    partitionByComponents p.NumVertices p.GraphEdges
    |> List.map (fun (verts, localEdges) -> { NumVertices = verts.Length; GraphEdges = localEdges })

// Per-part solver: a stand-in that counts edges; a real solver runs QAOA on the part
let solveOne (p: GraphProblem) : QuantumResult<int> = Ok p.GraphEdges.Length
let recombine (parts: int list) = List.sum parts

let largeProblem = { NumVertices = 5; GraphEdges = [ (0, 1); (1, 2); (3, 4) ] }

// Decomposes only if the problem exceeds the backend's MaxQubits
match solveWithDecomposition backend largeProblem estimateQubits decompose recombine solveOne with
| Ok total -> printfn "Edges handled: %d" total
| Error err -> printfn "Error: %s" err.Message
```

### Example: Connected Components

```fsharp
open FSharp.Azure.Quantum.Core.ProblemDecomposition

// Graph with two disconnected components: {0,1,2} and {3,4}
let edges = [(0, 1); (1, 2); (3, 4)]
let components = connectedComponents 5 edges
// components = [[0; 1; 2]; [3; 4]]

// Check if decomposition fits within a 3-qubit backend
let fits = canDecomposeWithinLimit 5 edges 3 1
// fits = true (largest component has 3 vertices × 1 qubit each = 3 ≤ 3)

// Get partitioned sub-problems with local indices
let parts = partitionByComponents 5 edges
// parts = [([0; 1; 2], [(0, 1); (1, 2)]); ([3; 4], [(0, 1)])]
```

---

## Advanced Topics

### Custom QAOA Parameters

The high-level builders use default QAOA settings. To choose them yourself, call the solver in `FSharp.Azure.Quantum.Quantum` directly:

```fsharp
open System.Threading
open FSharp.Azure.Quantum.Quantum

// Configure MaxCut QAOA behavior
let maxCutConfig : QuantumMaxCutSolver.QaoaConfig = {
    NumShots = 500                   // Number of measurement shots
    InitialParameters = (0.5, 0.5)   // Starting (gamma, beta)
}

// The solver-level problem has only vertices and edges
let solverProblem : QuantumMaxCutSolver.MaxCutProblem =
    { Vertices = problem_maxcut.Vertices; Edges = problem_maxcut.Edges }

// solveAsync returns a Task
let maxCutResult =
    QuantumMaxCutSolver.solveAsync backend solverProblem maxCutConfig CancellationToken.None
    |> Async.AwaitTask
    |> Async.RunSynchronously

match maxCutResult with
| Ok result -> 
    printfn "Cut value: %.2f" result.CutValue
    printfn "Partition: %A | %A" result.PartitionS result.PartitionT
| Error err ->
    printfn "Error: %s" err.Message
```

### Error Handling Patterns

```fsharp
// Pattern 1: Match on Result
task {
    match! GraphColoring.solveAsync problem 3 None CancellationToken.None with
    | Ok solution -> printfn "Colors used: %d" solution.ColorsUsed
    | Error err -> eprintfn "Failed: %s" err.Message
}

// Pattern 2: Result.map
task {
    let! result = GraphColoring.solveAsync problem 3 None CancellationToken.None
    return
        result
        |> Result.map (fun solution -> solution.Cost)
        |> Result.defaultValue infinity
}

// Pattern 3: Railway-oriented programming
let workflow p (cancellationToken: CancellationToken) =
    quantumResultTask {
        do! GraphColoring.validate p
        let! solution = GraphColoring.solveAsync p 3 None cancellationToken
        return solution.Assignments
    }
```

---

## Performance Tips

### 1. Start Small

```fsharp
// Test with a small instance on LocalBackend first (MaxCut needs at least one edge)
let testProblem = MaxCut.createProblem ["A"; "B"; "C"] [ ("A", "B", 1.0); ("B", "C", 1.0) ]
task {
    match! MaxCut.solveAsync testProblem None CancellationToken.None with
    | Ok _ -> 
        // Works! Now scale up: one qubit per vertex
        let ringVertices = [ for i in 1 .. 12 -> $"V{i}" ]
        let largeProblem = MaxCut.cycleGraph ringVertices 1.0
        printfn "Created larger problem with %d vertices" largeProblem.VertexCount
    | Error err -> printfn "Error: %s" err.Message
}
```

### 2. Use Problem Validation

```fsharp
// Validate before solving
let validated =
    task {
        match GraphColoring.validate problem with
        | Ok () -> 
            return! GraphColoring.solveAsync problem 3 None CancellationToken.None
        | Error err -> 
            return Error err
    }
```

### 3. Reuse a Backend

```fsharp
// Create once, reuse for many problems
let sharedBackend = LocalBackendFactory.createUnified()

let problems = [ problem; problem_scheduling ]

task {
    let solutions = ResizeArray()

    for p in problems do
        let! result = GraphColoring.solveAsync p 3 (Some sharedBackend) CancellationToken.None
        result |> Result.iter solutions.Add

    return List.ofSeq solutions
}
```

---

## OpenQASM Export

**Module:** `FSharp.Azure.Quantum.OpenQasmExport` (also re-exported as `OpenQasm`)

Export `CircuitBuilder.Circuit` values to OpenQASM (2.0 by default; 3.0 via `OpenQasm.exportV3` or a `QasmConfig` from `OpenQasmVersion.configFor V3_0`) for interoperability with other quantum frameworks. `OpenQasmImport.parse` / `parseFromFile` read OpenQASM back into a circuit.

```text
val export                 : circuit:Circuit -> string
val exportWithConfig       : config:QasmConfig -> circuit:Circuit -> string
val validate               : circuit:Circuit -> Result<unit, string>

// Sync
val exportToFile           : circuit:Circuit -> filePath:string -> unit
val exportToFileWithConfig : config:QasmConfig -> circuit:Circuit -> filePath:string -> unit

// Async
val exportToFileAsync           : circuit:Circuit -> filePath:string -> ct:CancellationToken -> Task<unit>
val exportToFileWithConfigAsync : config:QasmConfig -> circuit:Circuit -> filePath:string -> ct:CancellationToken -> Task<unit>
```

---

## Related Documentation

- [Getting Started Guide](getting-started) - Installation and setup
- [Architecture Overview](architecture-overview) - Library design
- [Backend Switching](backend-switching) - Local vs cloud backends, async patterns
- [QUBO Encoding Strategies](qubo-encoding-strategies) - Problem transformations
- [Quantum Machine Learning](quantum-machine-learning) - VQC, Quantum Kernels, Feature Maps
- [Business Problem Builders](business-problem-builders) - AutoML, Fraud Detection, Anomaly Detection
- [Error Mitigation](error-mitigation) - ZNE, PEC, REM strategies for NISQ hardware
- [Advanced Quantum Builders](advanced-quantum-builders) - Tree Search, Constraint Solver, Shor's Algorithm
- [FAQ](faq) - Common questions

---

**Last Updated**: 2026-09-30 (package version 1.4.15)
