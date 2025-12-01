---
layout: default
title: API Reference
---

# API Reference

Complete reference for **FSharp.Azure.Quantum** quantum optimization APIs.

## Table of Contents

- [Quick Start Patterns](#quick-start-patterns) - Common usage patterns
- [Graph Coloring Builder](#graph-coloring-builder) - Register allocation, scheduling
- [MaxCut Builder](#maxcut-builder) - Circuit partitioning, community detection
- [Knapsack Builder](#knapsack-builder) - Resource allocation, cargo loading
- [TSP Builder](#tsp-builder) - Route optimization, delivery planning
- [Portfolio Builder](#portfolio-builder) - Investment allocation, asset selection
- [Network Flow Builder](#network-flow-builder) - Supply chain optimization
- [Quantum Backends](#quantum-backends) - LocalBackend, IonQ, Rigetti
- [C# Interop](#c-interop) - Using from C#
- [Core Types](#core-types) - Data structures and result types
- **[QUBO Encoding Strategies](qubo-encoding-strategies.md)** - Problem transformations

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
| Error msg -> 
    printfn "Error: %s" msg
```

### Pattern 2: Cloud Backend (Large Problems)

```fsharp
// Create Azure Quantum backend
let backend = BackendAbstraction.createIonQBackend(
    connectionString = "YOUR_CONNECTION_STRING",
    targetId = "ionq.simulator"
)

// Solve on cloud quantum hardware
match GraphColoring.solve problem 3 (Some backend) with
| Ok solution -> 
    printfn "Colors used: %d" solution.ColorsUsed
    printfn "Valid: %b" solution.IsValid
| Error msg -> 
    printfn "Error: %s" msg
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
| Error msg -> printfn "Error: %s" msg
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
let problem = graphColoring {
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
val validate : GraphColoringProblem → Result<unit, string>
val solve : GraphColoringProblem → int → IQuantumBackend option → Result<ColoringSolution, string>
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
| Error msg ->
    printfn "Allocation failed: %s" msg
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
val solve : MaxCutProblem → IQuantumBackend option → Result<Solution, string>
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

let problem = MaxCut.createProblem vertices edges

match MaxCut.solve problem None with
| Ok solution ->
    printfn "Partition 1: %A" solution.PartitionS
    printfn "Partition 2: %A" solution.PartitionT
    printfn "Inter-partition traffic: %.2f" solution.CutValue
| Error msg ->
    printfn "Partitioning failed: %s" msg
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
val solve : Problem → IQuantumBackend option → Result<Solution, string>
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

let problem = Knapsack.createProblem cargo 300.0  // 300kg capacity

match Knapsack.solve problem None with
| Ok solution ->
    printfn "Total value: $%.2f" solution.TotalValue
    printfn "Weight: %.2f/%.2f kg" solution.TotalWeight problem.Capacity
    printfn "Efficiency: $%.2f/kg" solution.Efficiency
    
    solution.SelectedItems 
    |> List.iter (fun item -> 
        printfn "  Load: %s (%.2f kg, $%.2f)" item.Id item.Weight item.Value)
| Error msg ->
    printfn "Optimization failed: %s" msg
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
val solve : TspProblem → IQuantumBackend option → Result<Tour, string>
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

let problem = TSP.createProblem cities

match TSP.solve problem None with
| Ok tour ->
    printfn "Optimal route: %s" (String.concat " → " tour.Cities)
    printfn "Total distance: %.2f km" tour.TotalDistance
| Error msg ->
    printfn "Route optimization failed: %s" msg
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
val solve : PortfolioProblem → IQuantumBackend option → Result<PortfolioAllocation, string>
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

let problem = Portfolio.createProblem assets 50000.0  // $50k budget

match Portfolio.solve problem None with
| Ok allocation ->
    printfn "Portfolio value: $%.2f" allocation.TotalValue
    printfn "Expected return: %.2f%%" (allocation.ExpectedReturn * 100.0)
    printfn "Portfolio risk: %.2f" allocation.Risk
    
    allocation.Allocations 
    |> List.iter (fun (symbol, shares, value) ->
        printfn "  %s: %.2f shares = $%.2f" symbol shares value)
| Error msg ->
    printfn "Allocation failed: %s" msg
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
val solve : NetworkFlowProblem → IQuantumBackend option → Result<FlowSolution, string>
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
| Error msg ->
    printfn "Optimization failed: %s" msg
```

---

## Quantum Backends

**Module:** `FSharp.Azure.Quantum.Core.BackendAbstraction`

### LocalBackend

**Characteristics:**
- ✅ Free (local simulation)
- ✅ Fast (milliseconds)
- ✅ Up to 16 qubits
- ✅ Perfect for development/testing

```fsharp
let backend = BackendAbstraction.createLocalBackend()

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
let backend = BackendAbstraction.createIonQBackend(
    connectionString = "Endpoint=https://...",
    targetId = "ionq.simulator"  // or "ionq.qpu"
)

match GraphColoring.solve problem 3 (Some backend) with
| Ok solution -> 
    printfn "Executed on: %s" solution.BackendName
```

### RigettiBackend (Azure Quantum)

```fsharp
let backend = BackendAbstraction.createRigettiBackend(
    connectionString = "Endpoint=https://...",
    targetId = "rigetti.sim.qvm"  // or QPU target
)
```

### Backend Selection Guide

| Problem Size | Recommended Backend | Rationale |
|--------------|---------------------|-----------|
| ≤16 qubits | LocalBackend | Free, fast, sufficient |
| 17-20 qubits | IonQ/Rigetti Simulator | Scalable, still affordable |
| 20+ qubits | IonQ/Rigetti QPU | Real quantum hardware needed |

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

**See:** [C# Usage Guide](../CSHARP-QUANTUM-BUILDER-USAGE-GUIDE.md) for complete examples

---

## Core Types

### Result Type

All solvers return `Result<'T, string>`:

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

```fsharp
type IQuantumBackend =
    abstract Execute : Circuit -> int -> ExecutionResult
    abstract Name : string
```

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

## Advanced Topics

### Custom QAOA Parameters

```fsharp
open FSharp.Azure.Quantum.Quantum

// Configure optimization behavior
let config : QuantumMaxCutSolver.QuantumMaxCutConfig = {
    OptimizationShots = 50           // Shots per optimization iteration
    FinalShots = 500                 // Shots for final measurement
    EnableOptimization = true        // Enable parameter search
    InitialParameters = (0.5, 0.5)   // Starting (gamma, beta)
}

// Use with quantum solver directly
let backend = BackendAbstraction.createLocalBackend()
match QuantumMaxCutSolver.solve backend problem config with
| Ok result -> 
    let (gamma, beta) = result.OptimizedParameters
    printfn "Optimal parameters: γ=%.3f, β=%.3f" gamma beta
```

### Error Handling Patterns

```fsharp
// Pattern 1: Match on Result
match solver.solve problem with
| Ok solution -> processSuccess solution
| Error msg -> handleError msg

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
| Error msg -> 
    Error (sprintf "Invalid problem: %s" msg)
```

### 3. Cache Backends

```fsharp
// Create once, reuse many times
let backend = BackendAbstraction.createLocalBackend()

problems 
|> List.map (fun p -> GraphColoring.solve p 3 (Some backend))
|> List.choose Result.toOption
```

---

## Related Documentation

- [Getting Started Guide](getting-started) - Installation and setup
- [Architecture Overview](architecture-overview) - Library design
- [QUBO Encoding Strategies](qubo-encoding-strategies) - Problem transformations
- [FAQ](faq) - Common questions

---

**Last Updated**: 2025-11-29
