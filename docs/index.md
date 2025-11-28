---
layout: default
title: FSharp.Azure.Quantum
---

# FSharp.Azure.Quantum

**F# library for quantum-inspired optimization** - Solve TSP, Portfolio, and combinatorial optimization problems with automatic quantum vs classical routing.

[![NuGet](https://img.shields.io/nuget/v/FSharp.Azure.Quantum.svg)](https://www.nuget.org/packages/FSharp.Azure.Quantum/)
[![License](https://img.shields.io/badge/license-Unlicense-blue.svg)](https://github.com/thorium/FSharp.Azure.Quantum/blob/master/LICENSE)

## 🚀 Quick Start

```fsharp
open FSharp.Azure.Quantum.Classical

// Hybrid solver automatically chooses classical or quantum
let cities = [
    ("Seattle", 47.6, -122.3)
    ("Portland", 45.5, -122.7)
    ("San Francisco", 37.8, -122.4)
]

// Convert to distance matrix
let distances = TSP.calculateDistances cities

// Let HybridSolver decide the best approach
match HybridSolver.solveTsp distances None None None with
| Ok solution ->
    printfn "Method: %A" solution.Method  // Classical or Quantum
    printfn "Reasoning: %s" solution.Reasoning
    printfn "Best route: %A" solution.Result
| Error msg -> printfn "Error: %s" msg
```

## 📦 Installation

```bash
dotnet add package FSharp.Azure.Quantum
```

## ✨ Features

### 🔀 HybridSolver - Automatic Quantum/Classical Routing
Automatically chooses the best solver (quantum or classical) based on problem characteristics.

### 🗺️ TSP (Traveling Salesman Problem)
Solve routing problems with named cities and coordinates.

### 💼 Portfolio Optimization
Optimize asset allocation with budget and risk constraints.

### 🔬 Quantum Circuit Building
Build and validate quantum circuits with gate operations.

### 🧪 Local Quantum Simulation
Test quantum algorithms offline without Azure credentials - up to 10 qubits.

### 📊 QAOA Implementation
Quantum Approximate Optimization Algorithm for combinatorial problems.

## 📚 Documentation

- [Getting Started](getting-started) - Installation and first steps
- [Local Simulation](local-simulation) - Quantum simulation without Azure
- [Backend Switching](backend-switching) - Switch between local and Azure
- [API Reference](api-reference) - Complete API documentation
- [Examples](examples/tsp-example) - Practical examples and tutorials
- [FAQ](faq) - Frequently asked questions

## 🎯 Use Cases

- **Supply Chain Optimization** - Route planning and logistics
- **Financial Portfolio** - Asset allocation with constraints
- **Resource Scheduling** - Task and resource optimization
- **Network Design** - Optimal network topology

## 🔗 Links

- [GitHub Repository](https://github.com/thorium/FSharp.Azure.Quantum)
- [NuGet Package](https://www.nuget.org/packages/FSharp.Azure.Quantum/)
- [Report Issues](https://github.com/thorium/FSharp.Azure.Quantum/issues)

## 📄 License

This project is licensed under the [Unlicense](https://unlicense.org/) - dedicated to the public domain.
