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

**All FSharp.Azure.Quantum APIs use `QuantumResult<T>` with structured `QuantumError` types:**

```fsharp
// Type alias for clarity
type QuantumResult<'T> = Result<'T, QuantumError>
```

### Basic Error Handling

All solver APIs return `QuantumResult<T>` for consistent, type-safe error handling:

```fsharp
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.GraphColoring

match GraphColoring.solve problem 3 None with
| Ok solution -> 
    printfn "Success! Colors used: %d" solution.ColorsUsed
| Error err -> 
    printfn "Error: %s" err.Message  // Human-readable message
```

### QuantumError Types

Errors are categorized for precise handling:

```fsharp
type QuantumError =
    | ValidationError of field: string * reason: string
    | OperationError of operation: string * context: string
    | BackendError of backend: string * reason: string
    | IOError of operation: string * path: string * reason: string
    | NotImplemented of feature: string * hint: string option
    | Other of message: string
```

### Advanced Error Handling

Pattern match on error types for custom handling:

```fsharp
match TSP.solve cities None with
| Ok tour -> processTour tour
| Error (QuantumError.ValidationError (field, reason)) ->
    printfn "Invalid %s: %s" field reason
| Error (QuantumError.BackendError (backend, reason)) ->
    printfn "Backend %s failed: %s" backend reason
    // Retry with different backend
| Error err ->
    printfn "Unexpected error: %s" err.Message
```

### Computation Expression (Recommended)

Use the `quantumResult` builder to avoid nested match clauses:

```fsharp
let processWorkflow input backend = quantumResult {
    do! validateInput input
    let! encoded = encodeToQubo input
    let! result = executeQuantum encoded backend
    return result
}
```

See [QuantumResult Builder Guide](quantumresult-builder-guide.md) for complete details.

---

## Quick Start Patterns

### Pattern 1: Simple Auto-Solve (Recommended)

```fsharp
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.GraphColoring

// Graph Coloring: Uses LocalBackend automatically
let problem = graphColoring {
    node "R1" ["R2"; "R3"]
    node "R2" ["R1"]
    node "R3" ["R1"]
    colors ["Red"; "Blue"; "Green"]
}

match GraphColoring.solve problem 3 None with
| Ok solution -> 
    printfn "Colors used: %d" solution.ColorsUsed
    printfn "Valid: %b" solution.IsValid
| Error err -> 
    printfn "Error: %s" err.Message
```

### Pattern 2: Cloud Backend (Large Problems)

```fsharp
// Create Azure Quantum backend
let backend = // Cloud backend - requires Azure Quantum workspace
// BackendAbstraction.createIonQBackend(
    connectionString = "YOUR_CONNECTION_STRING",
    targetId = "ionq.simulator"
)

// Solve on cloud quantum hardware
match GraphColoring.solve problem 3 (Some backend) with
| Ok solution -> 
    printfn "Colors used: %d" solution.ColorsUsed
    printfn "Valid: %b" solution.IsValid
| Error err -> 
    printfn "Error: %s" err.Message
```

### Pattern 3: Inspect Solution Details

```fsharp
// Solve and inspect detailed results
match GraphColoring.solve problem 3 None with
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

```fsharp
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
val solve : GraphColoringProblem → int → IQuantumBackend option → QuantumResult<ColoringSolution>
```

**Parameters:**
- `problem` - Graph coloring problem specification
- `maxColors` - Maximum colors allowed (None = unlimited)
- `backend` - Quantum backend (None = auto LocalBackend)

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

match GraphColoring.solve registers 4 None with
| Ok solution ->
    printfn "Registers needed: %d" solution.ColorsUsed
    solution.Assignments 
    |> Map.iter (fun var reg -> printfn "%s → %s" var reg)
| Error err ->
    printfn "Allocation failed: %s" err.Message
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
val solve : MaxCutProblem → IQuantumBackend option → QuantumResult<Solution>
```

### Types

```fsharp
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

match MaxCut.solve problem_maxcut None with
| Ok solution ->
    printfn "Partition 1: %A" solution.PartitionS
    printfn "Partition 2: %A" solution.PartitionT
    printfn "Inter-partition traffic: %.2f" solution.CutValue
| Error err ->
    printfn "Partitioning failed: %s" err.Message
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
val solve : Problem → IQuantumBackend option → QuantumResult<Solution>
```

**Parameters:**
- `items` - (id, weight, value) tuples
- `capacity` - Maximum total weight

### Types

```fsharp
type Item = {
    Id: string
    Weight: float
    Value: float
}

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

match Knapsack.solve problem_knapsack None with
| Ok solution ->
    printfn "Total value: $%.2f" solution.TotalValue
    printfn "Weight: %.2f/%.2f kg" solution.TotalWeight problem_knapsack.Capacity
    printfn "Efficiency: $%.2f/kg" solution.Efficiency
    
    solution.SelectedItems 
    |> List.iter (fun item -> 
        printfn "  Load: %s (%.2f kg, $%.2f)" item.Id item.Weight item.Value)
| Error err ->
    printfn "Optimization failed: %s" err.Message
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
val solve : TspProblem → IQuantumBackend option → QuantumResult<Tour>
```

**Parameters:**
- `cities` - (name, x, y) coordinate tuples

### Types

```fsharp
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
// Delivery route optimization
let cities = [
    ("Warehouse", 0.0, 0.0)
    ("Customer A", 5.0, 3.0)
    ("Customer B", 2.0, 7.0)
    ("Customer C", 8.0, 4.0)
    ("Customer D", 3.0, 6.0)
]

let problem_tsp = TSP.createProblem cities

match TSP.solve problem_tsp None with
| Ok tour ->
    printfn "Optimal route: %s" (String.concat " → " tour.Cities)
    printfn "Total distance: %.2f km" tour.TotalDistance
| Error err ->
    printfn "Route optimization failed: %s" err.Message
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
val solve : PortfolioProblem → IQuantumBackend option → QuantumResult<PortfolioAllocation>
```

**Parameters:**
- `assets` - (symbol, expectedReturn, risk, price) tuples
- `budget` - Total available capital

### Types

```fsharp
type PortfolioProblem = {
    Assets: Asset array
    AssetCount: int
    Budget: float
    Constraints: Constraints option
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

match Portfolio.solve problem_portfolio None with
| Ok allocation ->
    printfn "Portfolio value: $%.2f" allocation.TotalValue
    printfn "Expected return: %.2f%%" (allocation.ExpectedReturn * 100.0)
    printfn "Portfolio risk: %.2f" allocation.Risk
    
    allocation.Allocations 
    |> List.iter (fun (symbol, shares, value) ->
        printfn "  %s: %.2f shares = $%.2f" symbol shares value)
| Error err ->
    printfn "Allocation failed: %s" err.Message
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

```fsharp
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
val SourceNode : string → int → Node
val SinkNode : string → int → Node
val IntermediateNode : string → int → Node
val Route : string → string → float → Route
val solve : NetworkFlowProblem → IQuantumBackend option → QuantumResult<FlowSolution>
```

### Example

```fsharp
// Supply chain optimization
let nodes = [
    NetworkFlow.SourceNode("Factory A", 1000)
    NetworkFlow.SourceNode("Factory B", 800)
    NetworkFlow.IntermediateNode("Warehouse", 1500)
    NetworkFlow.SinkNode("Store 1", 400)
    NetworkFlow.SinkNode("Store 2", 600)
    NetworkFlow.SinkNode("Store 3", 300)
]

let routes = [
    NetworkFlow.Route("Factory A", "Warehouse", 5.0)
    NetworkFlow.Route("Factory B", "Warehouse", 4.0)
    NetworkFlow.Route("Warehouse", "Store 1", 3.0)
    NetworkFlow.Route("Warehouse", "Store 2", 2.5)
    NetworkFlow.Route("Warehouse", "Store 3", 4.5)
]

let problem = { NetworkFlow.Nodes = nodes; Routes = routes }

match NetworkFlow.solve problem None with
| Ok flow ->
    printfn "Total cost: $%.2f" flow.TotalCost
    printfn "Fill rate: %.1f%%" (flow.FillRate * 100.0)
    
    flow.SelectedRoutes 
    |> List.iter (fun (from, to_, amount) ->
        printfn "  %s → %s: %.2f units" from to_ amount)
| Error err ->
    printfn "Optimization failed: %s" err.Message
```

---

## Quantum Backends

**Module:** `FSharp.Azure.Quantum.Core.BackendAbstraction`

### LocalBackend

**Characteristics:**
- ✅ Free (local simulation)
- ✅ Fast (milliseconds)
- ✅ Up to 20 qubits
- ✅ Perfect for development/testing

```fsharp
let backend = LocalBackendFactory.createUnified()

// Use with any solver
match GraphColoring.solve problem 3 (Some backend) with
| Ok solution -> printfn "Colors used: %d" solution.ColorsUsed
```

### IonQBackend (Azure Quantum)

**Characteristics:**
- ⚡ 29+ qubits (simulator)
- ⚡ 11 qubits (QPU hardware)
- 💰 Paid service
- ⏱️ Job queue (10-60 seconds)

```fsharp
let backend = // Cloud backend - requires Azure Quantum workspace
// BackendAbstraction.createIonQBackend(
    connectionString = "Endpoint=https://...",
    targetId = "ionq.simulator"  // or "ionq.qpu"
)

match GraphColoring.solve problem 3 (Some backend) with
| Ok solution -> 
    printfn "Executed on: %s" solution.BackendName
```

### RigettiBackend (Azure Quantum)

```fsharp
let backend = // Cloud backend - requires Azure Quantum workspace
// BackendAbstraction.createRigettiBackend(
    connectionString = "Endpoint=https://...",
    targetId = "rigetti.sim.qvm"  // or QPU target
)
```

### Backend Selection Guide

| Problem Size | Recommended Backend | Rationale |
|--------------|---------------------|-----------|
| ≤20 qubits | LocalBackend | Free, fast, sufficient |
| 17-29 qubits | IonQ/Rigetti Simulator | Scalable, still affordable |
| 30+ qubits | IonQ/Rigetti QPU | Real quantum hardware needed |

### IQubitLimitedBackend Interface

**Module:** `FSharp.Azure.Quantum.Core.BackendAbstraction`

Optional interface for backends that report qubit capacity limits. Solvers can test for this interface to query capacity without requiring all backends to implement it.

```fsharp
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

**Module:** `FSharp.Azure.Quantum.CSharpBuilders`

All problem builders have C#-friendly static methods:

```csharp
using FSharp.Azure.Quantum;
using static FSharp.Azure.Quantum.CSharpBuilders;

// MaxCut
var vertices = new[] { "A", "B", "C" };
var edges = new[] {
    (source: "A", target: "B", weight: 1.0),
    (source: "B", target: "C", weight: 2.0)
};
var problem = MaxCutProblem(vertices, edges);
var result = MaxCut.solve(problem, null);

// Knapsack
var items = new[] {
    (id: "laptop", weight: 3.0, value: 1000.0)
};
var problem = KnapsackProblem(items, capacity: 5.0);

// TSP
var cities = new[] {
    (name: "Seattle", x: 0.0, y: 0.0)
};
var problem = TspProblem(cities);

// Portfolio
var assets = new[] {
    (symbol: "AAPL", expectedReturn: 0.12, risk: 0.15, price: 150.0)
};
var problem = PortfolioProblem(assets, budget: 10000.0);
```

**See:** C# interoperability examples above for complete usage

---

## Core Types

### Result Type

All solvers return `QuantumResult<'T>`:

```fsharp
match solver.solve problem with
| Ok solution -> 
    // Success case
    printfn "Solution: %A" solution
| Error errorMessage -> 
    // Failure case
    printfn "Error: %s" errorMessage
```

### IQuantumBackend Interface

**Module:** `FSharp.Azure.Quantum.Core.BackendAbstraction`

```fsharp
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
```

See also `IQubitLimitedBackend` (inherits `IQuantumBackend`, adds `MaxQubits: int option`)
in the [Backend Selection Guide](#backend-selection-guide) section below.

### Circuit Types

```fsharp
type Gate =
    | H of int                       // Hadamard
    | RX of int * float              // Rotation-X
    | RY of int * float              // Rotation-Y
    | RZ of int * float              // Rotation-Z
    | CNOT of int * int              // Controlled-NOT
    | RZZ of int * int * float       // Two-qubit rotation

type QaoaLayer = {
    CostGates: Gate array
    MixerGates: Gate array
    Gamma: float
    Beta: float
}

type Circuit = {
    NumQubits: int
    InitialStateGates: Gate array
    Layers: QaoaLayer array
}
```

---

## Quantum Linear System Solver (HHL Algorithm)

**Module:** `FSharp.Azure.Quantum.QuantumLinearSystemSolver`

**Use Cases (Scientific & Engineering):**
- Machine learning: quantum SVM, least squares regression, PCA
- Engineering: solving PDEs/ODEs, finite element analysis, circuit simulation
- Finance: portfolio optimization with covariance matrices, risk modeling
- Chemistry: molecular dynamics, quantum chemistry simulations
- Data science: large-scale optimization, data fitting

**Algorithm:** HHL (Harrow-Hassidim-Lloyd) - solves Ax = b exponentially faster than classical methods

### What is HHL?

HHL solves linear systems **Ax = b** where:
- **Input**: Hermitian matrix A (N×N), vector |b⟩
- **Output**: Quantum state |x⟩ encoding solution
- **Speedup**: O(log N) vs O(N) classical - exponential for large sparse systems!

**Quantum Advantage:**
- Classical Gaussian elimination: O(N³) operations
- Quantum HHL: O(log(N) × poly(κ, 1/ε)) operations
- For N=1000, κ=10: ~10⁹ vs ~10³ operations (million-fold speedup!)

### Computation Expression API

```fsharp
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.QuantumLinearSystemSolver

// Simple 2×2 system: [[3,1],[1,3]] * x = [1,0]
let problem = linearSystemSolver {
    matrix [[3.0, 1.0]; [1.0, 3.0]]
    vector [1.0; 0.0]
    precision 4  // 4 eigenvalue qubits = 16 bins
}

match solve problem with
| Ok solution ->
    printfn "Success probability: %.4f" solution.SuccessProbability
    printfn "Condition number: %A" solution.ConditionNumber
    printfn "Gates used: %d" solution.GateCount
| Error err ->
    printfn "Error: %s" err.Message
```

### Advanced Configuration

```fsharp
// Diagonal system (faster, more accurate)
let problem = linearSystemSolver {
    diagonalMatrix [2.0; 4.0; 8.0; 16.0]  // Eigenvalues
    vector [1.0; 1.0; 1.0; 1.0]
    precision 8
    eigenvalueQubits 6                     // Override precision
    inversionMethod (ExactRotation 1.0)   // Exact vs linear approximation
    minEigenvalue 0.001                    // Stability threshold
    postSelection true                     // Higher accuracy, lower success rate
    backend ionQBackend                    // Cloud quantum hardware
    shots 2000                             // Measurement samples
}
```

### Types

```fsharp
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
    BackendName: string
    IsQuantum: bool
    Success: bool
    Message: string
}
```

### Functions

```text
val solve : LinearSystemProblem → QuantumResult<LinearSystemSolution>
val solve2x2 : float → float → float → float → float → float → QuantumResult<LinearSystemSolution>
val solveDiagonal : float list → float list → QuantumResult<LinearSystemSolution>
```

### Example: Engineering Simulation

```fsharp
// Solve heat equation discretization: Ax = b
// A = tridiagonal matrix (heat diffusion operator)
// b = boundary conditions

let heatDiffusion = linearSystemSolver {
    matrix [
        [2.0, -1.0,  0.0,  0.0]
        [-1.0, 2.0, -1.0,  0.0]
        [0.0, -1.0,  2.0, -1.0]
        [0.0,  0.0, -1.0,  2.0]
    ]
    vector [100.0; 0.0; 0.0; 50.0]  // Boundary temps
    precision 6
    minEigenvalue 0.01  // Avoid small eigenvalues
}

match solve heatDiffusion with
| Ok solution ->
    printfn "Temperature distribution computed!"
    printfn "Condition number: %.2f" (defaultArg solution.ConditionNumber 0.0)
    
    match solution.SolutionAmplitudes with
    | Some amplitudes ->
        amplitudes 
        |> Map.iter (fun idx amp -> 
            printfn "  Point %d: %.4f" idx amp.Magnitude)
    | None ->
        printfn "Use measurement statistics for cloud backends"
| Error err ->
    printfn "Simulation failed: %s" err.Message
```

### Example: Machine Learning (Least Squares)

```fsharp
// Solve normal equations: (X^T X) w = X^T y
// For linear regression: find weights w

let leastSquares = linearSystemSolver {
    // Covariance matrix X^T X (must be symmetric positive definite)
    matrix [
        [10.0,  5.0,  2.0]
        [ 5.0, 12.0,  3.0]
        [ 2.0,  3.0,  8.0]
    ]
    // Right-hand side X^T y
    vector [15.0; 20.0; 10.0]
    precision 8
    postSelection true  // Higher accuracy for ML
}

match solve leastSquares with
| Ok solution ->
    printfn "Model weights found!"
    printfn "Success rate: %.2f%%" (solution.SuccessProbability * 100.0)
| Error err ->
    printfn "Training failed: %s" err.Message
```

For a higher-level regression workflow (training config, intercept fitting, and metrics), see `FSharp.Azure.Quantum.MachineLearning.QuantumRegressionHHL` and `examples/MachineLearning/QuantumRegressionHHLExample.fsx`.

```fsharp
open FSharp.Azure.Quantum.MachineLearning

let config : QuantumRegressionHHL.RegressionConfig = {
    TrainX = [| [| 1.0 |]; [| 2.0 |]; [| 3.0 |] |]
    TrainY = [| 3.0; 5.0; 7.0 |]
    EigenvalueQubits = 4
    MinEigenvalue = 0.01
    Backend = backend
    Shots = 2000
    FitIntercept = true
    Verbose = false
}

match QuantumRegressionHHL.train config with
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
- Output is **quantum state |x⟩**, not classical vector
- Local simulation: get amplitude distribution
- Cloud backend: get measurement statistics (probabilities)
- Full state tomography needed for exact amplitudes (exponential cost!)

**Performance Considerations:**
- **Best for**: Large (N > 1000), sparse, well-conditioned systems
- **Condition number κ**: Lower is better (κ < 100 recommended)
- **Success probability**: ∝ 1/κ² (ill-conditioned = low success rate)
- **Practical speedup**: Requires large N with low condition number

### When to Use HHL vs Classical

| Problem Size | Condition Number | Sparsity | Recommendation |
|--------------|------------------|----------|----------------|
| N ≤ 100 | Any | Any | **Classical** (Gaussian elimination faster) |
| 100 < N ≤ 1000 | κ < 10 | Sparse | **HHL** (modest speedup) |
| N > 1000 | κ < 100 | Sparse | **HHL** (exponential speedup!) |
| Any N | κ > 1000 | Any | **Classical** (HHL success rate too low) |

---

## QAOA Execution Helpers

**Module:** `FSharp.Azure.Quantum.Core.QaoaExecutionHelpers`

Shared QAOA execution infrastructure for all quantum solvers. Consolidates QAOA circuit construction, parameter optimization, and measurement into reusable functions. Supports both dense (`float[,]`) and sparse (`Map<int * int, float>`) QUBO representations, and provides budget-constrained execution with backend capacity checking.

### Configuration Types

```fsharp
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
val defaultConfig     : QaoaSolverConfig   // Balanced (2 layers, 100/1000 shots, optimization on)
val fastConfig        : QaoaSolverConfig   // Quick prototyping (1 layer, 50/500 shots, grid search)
val highQualityConfig : QaoaSolverConfig   // Production (3 layers, 200/2000 shots, optimization on)
```

### Dense QUBO Functions

```text
val evaluateQubo :
    qubo:float[,] → bits:int[] → float

val executeQaoaCircuit :
    backend:IQuantumBackend → problemHam:ProblemHamiltonian → mixerHam:MixerHamiltonian
    → parameters:(float * float)[] → shots:int → Result<int[][], QuantumError>

val executeQaoaWithOptimization :
    backend:IQuantumBackend → qubo:float[,] → config:QaoaSolverConfig
    → Result<int[] * (float * float)[] * bool, QuantumError>

val executeQaoaWithGridSearch :
    backend:IQuantumBackend → qubo:float[,] → config:QaoaSolverConfig
    → Result<int[] * (float * float)[], QuantumError>

val executeFromQubo :
    backend:IQuantumBackend → qubo:float[,] → parameters:(float * float)[] → shots:int
    → Result<int[][], QuantumError>
```

**Parameters:**
- `qubo` — Dense QUBO matrix (`float[,]`)
- `config` — QAOA solver configuration
- `backend` — Quantum backend (explicit; RULE 1 compliance)

### Sparse QUBO Functions

Memory-efficient path that avoids allocating dense `float[,]` arrays. Preferred for large, sparse QUBO problems.

```text
val evaluateQuboSparse :
    quboMap:Map<int * int, float> → bits:int[] → float

val executeQaoaCircuitSparse :
    backend:IQuantumBackend → numQubits:int → quboMap:Map<int * int, float>
    → parameters:(float * float)[] → shots:int → Result<int[][], QuantumError>

val executeQaoaWithOptimizationSparse :
    backend:IQuantumBackend → numQubits:int → quboMap:Map<int * int, float>
    → config:QaoaSolverConfig → Result<int[] * (float * float)[] * bool, QuantumError>

val executeQaoaWithGridSearchSparse :
    backend:IQuantumBackend → numQubits:int → quboMap:Map<int * int, float>
    → config:QaoaSolverConfig → Result<int[] * (float * float)[], QuantumError>
```

**Parameters:**
- `numQubits` — Number of qubits (variables) in the QUBO
- `quboMap` — Sparse QUBO as `Map<(i, j), coefficient>` (only non-zero entries)

### Budget Execution Types

```fsharp
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

val executeWithBudget :
    backend:IQuantumBackend → qubo:float[,] → config:QaoaSolverConfig
    → budget:ExecutionBudget → Result<int[] * (float * float)[] * bool, QuantumError>
```

### Example: Sparse QUBO Execution

```fsharp
open FSharp.Azure.Quantum.Core.QaoaExecutionHelpers

let backend = LocalBackendFactory.createUnified()

// Define a sparse QUBO (only non-zero entries)
let quboMap =
    Map.ofList [
        (0, 0), -1.0
        (1, 1), -1.0
        (0, 1),  2.0
    ]

let config = defaultConfig

match executeQaoaWithOptimizationSparse backend 2 quboMap config with
| Ok (bestBits, parameters, converged) ->
    let energy = evaluateQuboSparse quboMap bestBits
    printfn "Best bitstring: %A" bestBits
    printfn "Energy: %.4f" energy
    printfn "Converged: %b" converged
| Error err ->
    printfn "Error: %s" err.Message
```

### Example: Budget-Constrained Execution

```fsharp
open FSharp.Azure.Quantum.Core.QaoaExecutionHelpers

let backend = LocalBackendFactory.createUnified()
let qubo = Array2D.init 4 4 (fun i j -> if i = j then -1.0 elif abs (i - j) = 1 then 0.5 else 0.0)

let budget = {
    MaxTotalShots = 500
    MaxTimeMs = Some 5000       // 5-second wall-clock limit
    Decomposition = AdaptiveToBudgetBackend
}

match executeWithBudget backend qubo defaultConfig budget with
| Ok (bits, params, converged) ->
    printfn "Solution: %A (converged=%b)" bits converged
| Error err ->
    printfn "Budget execution failed: %s" err.Message
```

---

## Problem Decomposition

**Module:** `FSharp.Azure.Quantum.Core.ProblemDecomposition`

Generic problem decomposition orchestrator for QAOA solvers. When a problem requires more qubits than the backend supports (`IQubitLimitedBackend.MaxQubits`), automatically splits the problem into sub-problems, solves them independently, and recombines the results. Fully generic over problem and solution types — solvers supply decompose/recombine/solve functions.

### Strategy Types

```fsharp
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

let backend = LocalBackendFactory.createUnified()

// Solver-supplied functions
let estimateQubits problem = problem.VertexCount
let decompose problem =
    partitionByComponents problem.VertexCount problem.Edges
    |> List.map (fun (verts, edges) -> { VertexCount = verts.Length; Edges = edges })
let recombine solutions = solutions |> List.reduce mergeSolutions
let solveOne problem = solveSmallProblem backend problem

// Automatically decomposes if problem exceeds backend capacity
match solveWithDecomposition backend largeProblem estimateQubits decompose recombine solveOne with
| Ok solution -> printfn "Solution: %A" solution
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

```fsharp
open FSharp.Azure.Quantum.Quantum

// Configure MaxCut QAOA behavior
let config : QuantumMaxCutSolver.QaoaConfig = {
    NumShots = 500                   // Number of measurement shots
    InitialParameters = (0.5, 0.5)   // Starting (gamma, beta)
}

// Use with quantum solver directly
let backend = LocalBackendFactory.createUnified()
match QuantumMaxCutSolver.solve backend problem config with
| Ok result -> 
    printfn "Cut value: %d" result.CutValue
    printfn "Partition: %A" result.Partition
| Error err ->
    printfn "Error: %A" err
```

### Error Handling Patterns

```fsharp
// Pattern 1: Match on Result
match solver.solve problem with
| Ok solution -> processSuccess solution
| Error err -> handleError msg

// Pattern 2: Result.map
problem
|> solver.solve
|> Result.map (fun solution -> solution.Cost)
|> Result.defaultValue infinity

// Pattern 3: Railway-oriented programming
let workflow problem =
    problem
    |> validate
    |> Result.bind solve
    |> Result.map postProcess
```

---

## Performance Tips

### 1. Start Small

```fsharp
// Test with LocalBackend first
let testProblem = MaxCut.createProblem ["A"; "B"; "C"] []
match MaxCut.solve testProblem None with
| Ok _ -> 
    // Works! Now scale up
    let largeProblem = MaxCut.createProblem largeVertices largeEdges
    printfn "Created large problem with %d vertices" largeVertices.Length
```

### 2. Use Problem Validation

```fsharp
// Validate before solving
match GraphColoring.validate problem with
| Ok () -> 
    GraphColoring.solve problem 3 None
| Error err -> 
    Error (sprintf "Invalid problem: %s" err.Message)
```

### 3. Cache Backends

```fsharp
// Create once, reuse many times
let backend = LocalBackendFactory.createUnified()

problems 
|> List.map (fun p -> GraphColoring.solve p 3 (Some backend))
|> List.choose Result.toOption
```

---

## Related Documentation

- [Getting Started Guide](getting-started) - Installation and setup
- [Architecture Overview](architecture-overview) - Library design
- [QUBO Encoding Strategies](qubo-encoding-strategies) - Problem transformations
- [Quantum Machine Learning](quantum-machine-learning) - VQC, Quantum Kernels, Feature Maps
- [Business Problem Builders](business-problem-builders) - AutoML, Fraud Detection, Anomaly Detection
- [Error Mitigation](error-mitigation) - ZNE, PEC, REM strategies for NISQ hardware
- [Advanced Quantum Builders](advanced-quantum-builders) - Tree Search, Constraint Solver, Shor's Algorithm
- [FAQ](faq) - Common questions

---

**Last Updated**: 2026-02-21
