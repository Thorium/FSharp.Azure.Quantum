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

### 🎯 6 Quantum Optimization Builders

**Production-ready quantum algorithms for common combinatorial problems:**

1. **Graph Coloring** - Register allocation, frequency assignment, scheduling
2. **MaxCut** - Circuit partitioning, community detection, load balancing
3. **Knapsack** - Resource allocation, cargo loading, project selection
4. **TSP** - Route optimization, delivery planning, logistics
5. **Portfolio** - Investment allocation, asset selection, risk management
6. **Network Flow** - Supply chain optimization, distribution planning

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
- ✅ Variational parameter optimization (Nelder-Mead)
- ✅ Configurable circuit depth and shot counts
- ✅ Solution validation and quality metrics

### 🖥️ Multiple Execution Backends

- **LocalBackend** - Fast simulation (≤16 qubits, free)
- **IonQBackend** - Azure Quantum (29+ qubits simulator, 11 qubits QPU)
- **RigettiBackend** - Azure Quantum (40+ qubits simulator, 80 qubits QPU)

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

## 🏗️ Architecture

**3-Layer Quantum-Only Design:**

```
┌─────────────────────────────────────────┐
│   Layer 1: High-Level Builders         │
│   (graphColoring { }, MaxCut.solve)    │
│   - F# computation expressions          │
│   - C# fluent APIs                      │
│   - Domain-specific validation          │
└─────────────────┬───────────────────────┘
                  │
┌─────────────────▼───────────────────────┐
│   Layer 2: Quantum Solvers (QAOA)      │
│   (QuantumGraphColoringSolver, etc.)    │
│   - QUBO encoding                       │
│   - Circuit construction                │
│   - Parameter optimization              │
└─────────────────┬───────────────────────┘
                  │
┌─────────────────▼───────────────────────┐
│   Layer 3: Quantum Backends             │
│   (LocalBackend, IonQ, Rigetti)         │
│   - State vector simulation             │
│   - Cloud quantum hardware              │
│   - Circuit execution                   │
└─────────────────────────────────────────┘
```

**Design Philosophy:**
- ✅ **Quantum-Only**: No classical algorithms (pure quantum optimization library)
- ✅ **Clear Layers**: No leaky abstractions between layers
- ✅ **Type-Safe**: F# type system prevents invalid problem specifications
- ✅ **Extensible**: Easy to add new problem types following existing patterns

## 📚 Documentation

### Getting Started
- [Getting Started Guide](getting-started) - Installation and first steps
- [Quantum Computing Introduction](quantum-computing-introduction) - Comprehensive introduction to quantum computing for F# developers (no quantum background needed)
- [C# Usage Guide](../CSHARP-QUANTUM-BUILDER-USAGE-GUIDE.md) - Complete C# interop examples

### Core Concepts
- [API Reference](api-reference) - Complete API documentation
- [Architecture Overview](architecture-overview) - Deep dive into library design
- [Backend Switching](backend-switching) - Local vs Cloud quantum execution

### Advanced Topics
- [QUBO Encoding Strategies](qubo-encoding-strategies) - Problem transformations
- [FAQ](faq) - Frequently asked questions

### Problem-Specific Guides
- [Graph Coloring API](GraphColoring-API) - Register allocation, scheduling
- [Quantum Chemistry API](QuantumChemistry-API) - VQE for molecular simulation
- [Task Scheduling API](TaskScheduling-API) - Constraint-based scheduling

### Examples
- [TSP Example](examples/tsp-example) - Route optimization tutorial
- [Quantum TSP Example](examples/quantum-tsp-example) - QAOA deep dive
- [Subset Selection Example](examples/subset-selection-example) - Knapsack variants

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
- **LocalBackend (default)** provides free, fast quantum simulation up to 16 qubits - ideal for development, testing, and many production use cases
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
- ✅ Up to 16 qubits
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
| Graph Coloring | ≤16 nodes | 20+ nodes |
| MaxCut | ≤16 vertices | 20+ vertices |
| Knapsack | ≤16 items | 20+ items |
| TSP | ≤6 cities | 8+ cities |
| Portfolio | ≤16 assets | 20+ assets |
| Network Flow | ≤12 nodes | 16+ nodes |

**Note:** LocalBackend supports up to 16 qubits. Larger problems require cloud backends.

## 📄 License

This project is licensed under the [Unlicense](https://unlicense.org/) - dedicated to the public domain.

---

**Status**: Production Ready - Quantum-only architecture with 6 problem builders

**Last Updated**: 2025-11-29
