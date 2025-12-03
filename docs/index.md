---
layout: default
title: FSharp.Azure.Quantum
---

# FSharp.Azure.Quantum

**Quantum-First F# Library** - Solve combinatorial optimization problems using QAOA (Quantum Approximate Optimization Algorithm) with automatic backend selection.

[![NuGet](https://img.shields.io/nuget/v/FSharp.Azure.Quantum.svg)](https://www.nuget.org/packages/FSharp.Azure.Quantum/)
[![License](https://img.shields.io/badge/license-Unlicense-blue.svg)](https://github.com/thorium/FSharp.Azure.Quantum/blob/master/LICENSE)

## 🚀 Quick Start

### F# Computation Expressions

```fsharp
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.GraphColoring

// Graph Coloring: Register Allocation
let problem = graphColoring {
    node "R1" ["R2"; "R3"]
    node "R2" ["R1"; "R4"]
    node "R3" ["R1"; "R4"]
    node "R4" ["R2"; "R3"]
    colors ["EAX"; "EBX"; "ECX"; "EDX"]
}

// Solve using quantum optimization (QAOA)
match GraphColoring.solve problem 4 None with
| Ok solution ->
    printfn "Colors used: %d" solution.ColorsUsed
    solution.Assignments 
    |> Map.iter (fun node color -> printfn "%s → %s" node color)
| Error msg -> 
    printfn "Error: %s" msg
```

### C# Fluent API

```csharp
using FSharp.Azure.Quantum;
using static FSharp.Azure.Quantum.CSharpBuilders;

// MaxCut: Circuit Partitioning
var vertices = new[] { "A", "B", "C", "D" };
var edges = new[] {
    (source: "A", target: "B", weight: 1.0),
    (source: "B", target: "C", weight: 2.0),
    (source: "C", target: "D", weight: 1.0),
    (source: "D", target: "A", weight: 1.0)
};

var problem = MaxCutProblem(vertices, edges);
var result = MaxCut.solve(problem, null);

if (result.IsOk) {
    var solution = result.ResultValue;
    Console.WriteLine($"Cut Value: {solution.CutValue}");
    Console.WriteLine($"Partition S: {string.Join(", ", solution.PartitionS)}");
    Console.WriteLine($"Partition T: {string.Join(", ", solution.PartitionT)}");
}
```

## 📦 Installation

```bash
dotnet add package FSharp.Azure.Quantum
```

## ✨ Features

### 🎯 7 Quantum Optimization Builders

**Production-ready quantum algorithms for common combinatorial problems:**

1. **Graph Coloring** - Register allocation, frequency assignment, scheduling
2. **MaxCut** - Circuit partitioning, community detection, load balancing
3. **Knapsack** - Resource allocation, cargo loading, project selection
4. **TSP** - Route optimization, delivery planning, logistics
5. **Portfolio** - Investment allocation, asset selection, risk management
6. **Network Flow** - Supply chain optimization, distribution planning
7. **Task Scheduling** - Manufacturing workflows, project management, resource allocation with dependencies

### 🧠 Quantum Machine Learning

**Apply quantum computing to machine learning:**

- ✅ **Variational Quantum Classifier (VQC)** - Supervised learning with quantum circuits
- ✅ **Quantum Kernel SVM** - Support vector machines with quantum feature spaces
- ✅ **Feature Maps** - ZZFeatureMap, PauliFeatureMap for data encoding
- ✅ **Variational Forms** - RealAmplitudes, EfficientSU2 ansatz circuits
- ✅ **Adam Optimizer** - Gradient-based training
- ✅ **Model Serialization** - Save/load trained models

**Examples:** `examples/QML/` (VQCExample, FeatureMapExample, VariationalFormExample)

### 📊 Business Problem Builders

**High-level APIs for business applications:**

- ✅ **AutoML** - Automated machine learning with quantum kernels
- ✅ **Anomaly Detection** - Security threat detection, fraud prevention
- ✅ **Binary Classification** - Fraud detection, spam filtering
- ✅ **Predictive Modeling** - Customer churn, demand forecasting
- ✅ **Similarity Search** - Product recommendations, semantic search

**Examples:** `examples/AutoML/`, `examples/AnomalyDetection/`, `examples/BinaryClassification/`, `examples/PredictiveModeling/`

### 🤖 HybridSolver - Optional Smart Routing

**Optional optimization layer for variable-sized problems:**

- ✅ **Analyzes problem size** - Routes small problems (< 20 variables) to classical fallback
- ✅ **Quantum-first** - Uses QAOA on LocalBackend/Cloud for >= 20 variables
- ✅ **Cost guards** - Budget limits prevent runaway quantum costs
- ✅ **Transparent reasoning** - Explains routing decision
- ✅ **Production-ready** - Useful when problem sizes vary significantly

**Recommendation:** Use direct quantum API (`GraphColoring.solve`, `MaxCut.solve`, etc.) for most cases. HybridSolver adds classical fallback optimization for very small problems.

**See:** [Getting Started Guide](getting-started) for detailed examples and decision criteria

### 🔬 QAOA Implementation

Quantum Approximate Optimization Algorithm with:
- ✅ Automatic QUBO encoding
- ✅ Advanced parameter optimization (COBYLA, SPSA, gradient-free)
- ✅ Configurable circuit depth and shot counts
- ✅ Solution validation and quality metrics
- ✅ Integer variable support

**Example:** `examples/QaoaParameterOptimizationExample.fsx`

### 🖥️ Multiple Execution Backends

- **LocalBackend** - Fast simulation (≤20 qubits, free)
- **IonQBackend** - Azure Quantum (29+ qubits simulator, 11 qubits QPU)
- **RigettiBackend** - Azure Quantum (40+ qubits simulator, 80 qubits QPU)
- **DWaveBackend** - D-Wave quantum annealer (2000+ qubits, production hardware)

### 💻 Cross-Language Support

- **F# First** - Idiomatic computation expressions and type safety
- **C# Friendly** - Fluent API extensions with value tuples
- **Seamless Interop** - Works naturally in both languages

## 🎯 Problem Types & Examples

### Graph Coloring

```fsharp
// Register allocation for compiler optimization
let registers = graphColoring {
    node "R1" ["R2"; "R3"; "R5"]
    node "R2" ["R1"; "R4"]
    node "R3" ["R1"; "R4"; "R5"]
    node "R4" ["R2"; "R3"]
    node "R5" ["R1"; "R3"]
    colors ["EAX"; "EBX"; "ECX"; "EDX"]
    objective MinimizeColors
}
```

### MaxCut

```fsharp
// Network community detection
let vertices = ["User1"; "User2"; "User3"; "User4"]
let edges = [
    ("User1", "User2", 3.0)  // interaction strength
    ("User2", "User3", 5.0)
    ("User3", "User4", 2.0)
    ("User4", "User1", 4.0)
]

let problem = MaxCut.createProblem vertices edges
let solution = MaxCut.solve problem None
```

### Knapsack

```fsharp
// Cargo loading optimization
let items = [
    ("Electronics", 50.0, 10000.0)
    ("Furniture", 200.0, 5000.0)
    ("Textiles", 30.0, 3000.0)
    ("Machinery", 150.0, 8000.0)
]

let problem = Knapsack.createProblem items 300.0  // 300kg capacity
```

### TSP

```fsharp
// Delivery route optimization
let cities = [
    ("Warehouse", 0.0, 0.0)
    ("Store A", 5.0, 3.0)
    ("Store B", 2.0, 7.0)
    ("Store C", 8.0, 4.0)
]

let problem = TSP.createProblem cities
```

### Portfolio

```fsharp
// Investment allocation
let assets = [
    ("Tech ETF", 0.15, 0.20, 100.0)    // return, risk, price
    ("Bonds", 0.05, 0.05, 50.0)
    ("Real Estate", 0.10, 0.12, 200.0)
]

let problem = Portfolio.createProblem assets 10000.0  // $10k budget
```

### Network Flow

```fsharp
// Supply chain optimization
let nodes = [
    { NetworkFlow.Id = "Factory"; NodeType = NetworkFlow.Source; Capacity = 1000 }
    { NetworkFlow.Id = "Warehouse"; NodeType = NetworkFlow.Intermediate; Capacity = 800 }
    { NetworkFlow.Id = "Customer"; NodeType = NetworkFlow.Sink; Capacity = 500 }
]

let routes = [
    { NetworkFlow.From = "Factory"; To = "Warehouse"; Cost = 5.0 }
    { NetworkFlow.From = "Warehouse"; To = "Customer"; Cost = 3.0 }
]

let problem = { NetworkFlow.Nodes = nodes; Routes = routes }
```

### Task Scheduling

```fsharp
// Manufacturing workflow with dependencies
let taskA = scheduledTask {
    taskId "TaskA"
    duration (hours 2.0)
    priority 10.0
}

let taskB = scheduledTask {
    taskId "TaskB"
    duration (hours 1.5)
    after "TaskA"  // Must wait for A
    requires "Worker" 2.0
    deadline 180.0
}

let problem = scheduling {
    tasks [taskA; taskB]
    objective MinimizeMakespan
}

// Solve with quantum backend for resource constraints
let backend = BackendAbstraction.createLocalBackend()
match solveQuantum backend problem with
| Ok solution ->
    printfn "Makespan: %.2f" solution.Makespan
| Error msg -> printfn "Error: %s" msg
```

## 🏗️ Architecture

**3-Layer Quantum-Only Design:**

![3-Layer Quantum Architecture](images/3-layer-architecture.svg)

**Design Philosophy:**
- ✅ **Quantum-Only**: No classical algorithms (pure quantum optimization library)
- ✅ **Clear Layers**: No leaky abstractions between layers
- ✅ **Type-Safe**: F# type system prevents invalid problem specifications
- ✅ **Extensible**: Easy to add new problem types following existing patterns

## 📚 Complete Documentation

### 🚀 Getting Started
- [Getting Started Guide](getting-started) - Installation, first steps, and basic examples
- [Quantum Computing Introduction](quantum-computing-introduction) - Comprehensive introduction to quantum computing for F# developers (no quantum background needed)
- [C# Usage Guide](../CSHARP-QUANTUM-BUILDER-USAGE-GUIDE.md) - Complete C# interop examples with fluent API

### 📖 Core Concepts
- [API Reference](api-reference) - Complete API documentation for all modules
- [Computation Expressions Reference](computation-expressions-reference) - Complete CE reference table with all custom operations (when IntelliSense fails)
- [Architecture Overview](architecture-overview) - Deep dive into 3-layer quantum-only design
- [Backend Switching](backend-switching) - Local vs Cloud vs D-Wave quantum execution
- [Local Simulation](local-simulation) - LocalBackend internals and performance characteristics

### 🔬 Advanced Topics
- [QUBO Encoding Strategies](qubo-encoding-strategies) - Problem-to-QUBO transformations for QAOA
- [Quantum Machine Learning Guide](qml-guide) - VQC, Quantum Kernels, Feature Maps (coming soon)
- [D-Wave Integration Guide](dwave-integration) - Using D-Wave quantum annealers (coming soon)
- [FAQ](faq) - Frequently asked questions and troubleshooting

### 🎯 Problem-Specific API Guides
- [Graph Coloring API](GraphColoring-API) - Register allocation, frequency assignment, scheduling
- [Quantum Chemistry API](QuantumChemistry-API) - VQE for molecular ground state energies
- [Task Scheduling API](TaskScheduling-API) - Constraint-based quantum scheduling

### 💡 Working Code Examples

**View source code on GitHub:**

#### Optimization Problems (QAOA)
- [**DeliveryRouting**](https://github.com/Thorium/FSharp.Azure.Quantum/tree/main/examples/DeliveryRouting) - TSP with 16-city NYC routing, HybridSolver
- [**InvestmentPortfolio**](https://github.com/Thorium/FSharp.Azure.Quantum/tree/main/examples/InvestmentPortfolio) - Portfolio optimization with constraints (F#)
- [**InvestmentPortfolio_CSharp**](https://github.com/Thorium/FSharp.Azure.Quantum/tree/main/examples/InvestmentPortfolio_CSharp) - Portfolio optimization (C# version)
- [**GraphColoring**](https://github.com/Thorium/FSharp.Azure.Quantum/tree/main/examples/GraphColoring) - Graph coloring with QAOA
- [**MaxCut**](https://github.com/Thorium/FSharp.Azure.Quantum/tree/main/examples/MaxCut) - Max-Cut problem with QAOA
- [**Knapsack**](https://github.com/Thorium/FSharp.Azure.Quantum/tree/main/examples/Knapsack) - 0/1 Knapsack optimization
- [**SupplyChain**](https://github.com/Thorium/FSharp.Azure.Quantum/tree/main/examples/SupplyChain) - Multi-constraint resource allocation
- [**JobScheduling**](https://github.com/Thorium/FSharp.Azure.Quantum/tree/main/examples/JobScheduling) - Task scheduling with dependencies

#### Advanced Quantum Algorithms
- [**QuantumChemistry**](https://github.com/Thorium/FSharp.Azure.Quantum/tree/main/examples/QuantumChemistry) - VQE for molecular simulation
- [**PhaseEstimation**](https://github.com/Thorium/FSharp.Azure.Quantum/tree/main/examples/PhaseEstimation) - Quantum Phase Estimation (QPE)
- [**QuantumArithmetic**](https://github.com/Thorium/FSharp.Azure.Quantum/tree/main/examples/QuantumArithmetic) - QFT-based arithmetic operations
- [**CryptographicAnalysis**](https://github.com/Thorium/FSharp.Azure.Quantum/tree/main/examples/CryptographicAnalysis) - Shor's algorithm demonstrations

#### Interactive Demonstrations
- [**Gomoku**](https://github.com/Thorium/FSharp.Azure.Quantum/tree/main/examples/Gomoku) - Quantum vs Classical AI game (with Hybrid mode)
- [**Kasino**](https://github.com/Thorium/FSharp.Azure.Quantum/tree/main/examples/Kasino) - Quantum gambling game demonstrating superposition (F#)
- [**Kasino_CSharp**](https://github.com/Thorium/FSharp.Azure.Quantum/tree/main/examples/Kasino_CSharp) - Quantum gambling game (C# version)

#### Example Navigation Guides
- [TSP Example Guide](examples/tsp-example) - Route optimization overview and quickstart
- [Quantum TSP Guide](examples/quantum-tsp-example) - QAOA parameter optimization deep dive
- [Subset Selection Guide](examples/subset-selection-example) - Knapsack and portfolio patterns

## 🎯 When to Use This Library

### ✅ Use FSharp.Azure.Quantum When:

- You want to learn quantum optimization algorithms (QAOA)
- You're building quantum-enabled applications
- You need quantum solutions for combinatorial problems
- You're researching quantum algorithm performance
- You want to experiment with quantum computing

### 🔄 Consider Classical Libraries When:

- Problem size < 50 variables (classical is faster)
- You need immediate results (< 1 second)
- Cost is a primary concern
- Deterministic results required

**Best Practice**: 
- **Use direct quantum API** (`GraphColoring.solve`, `MaxCut.solve`, etc.) for consistent quantum experience across all problem sizes
- **Use HybridSolver** only if you need automatic classical fallback for very small problems (< 20 variables)
- **LocalBackend (default)** provides free, fast quantum simulation up to 20 qubits - ideal for development, testing, and many production use cases
- **Cloud backends** (IonQ, Rigetti) for larger problems or real quantum hardware experimentation

## 🔧 Backend Selection Guide

### LocalBackend (Default)

```fsharp
// Automatic: No backend parameter needed
match MaxCut.solve problem None with
| Ok solution -> printfn "Max cut value: %f" solution.CutValue
| Error msg -> eprintfn "Error: %s" msg
```

**Characteristics:**
- ✅ Free (local simulation)
- ✅ Fast (milliseconds)
- ✅ Up to 20 qubits
- ✅ Perfect for development and testing

### Azure Quantum (Cloud)

```fsharp
// Create cloud backend
let backend = BackendAbstraction.createIonQBackend(
    connectionString = "YOUR_CONNECTION_STRING",
    targetId = "ionq.simulator"  // or "ionq.qpu"
)

// Pass to solver
match MaxCut.solve problem (Some backend) with
| Ok solution -> printfn "Max cut value: %f" solution.CutValue
| Error msg -> eprintfn "Error: %s" msg
```

**Characteristics:**
- ⚡ Scalable (29+ qubits)
- ⚡ Real quantum hardware available
- 💰 Paid service (~$10-100 per run)
- ⏱️ Slower (job queue, 10-60 seconds)

## 🤝 Contributing

Contributions welcome! See [GitHub Repository](https://github.com/thorium/FSharp.Azure.Quantum) for contribution guidelines.

**Areas we'd love help with:**
- New problem builders (SAT, Job Shop Scheduling, Vehicle Routing)
- QAOA warm-start strategies
- Alternative quantum algorithms (VQE, QASM)
- Additional cloud backend support (AWS Braket, IBM Quantum)
- Performance optimizations

## 🔗 Links

- [GitHub Repository](https://github.com/thorium/FSharp.Azure.Quantum)
- [NuGet Package](https://www.nuget.org/packages/FSharp.Azure.Quantum/)
- [Report Issues](https://github.com/thorium/FSharp.Azure.Quantum/issues)
- [API Documentation](api-reference)

## 📊 Performance Guidelines

| Problem Type | LocalBackend | Cloud Required |
|--------------|--------------|----------------|
| Graph Coloring | ≤20 nodes | 25+ nodes |
| MaxCut | ≤20 vertices | 25+ vertices |
| Knapsack | ≤20 items | 25+ items |
| TSP | ≤8 cities | 10+ cities |
| Portfolio | ≤20 assets | 25+ assets |
| Network Flow | ≤15 nodes | 20+ nodes |
| Task Scheduling | ≤15 tasks | 20+ tasks |

**Note:** LocalBackend supports up to 20 qubits. Larger problems require cloud backends.

## 📄 License

This project is licensed under the [Unlicense](https://unlicense.org/) - dedicated to the public domain.

---

**Status**: Production Ready - Quantum-only architecture with 7 problem builders

**Last Updated**: 2025-12-03
