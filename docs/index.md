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

// Solve using quantum optimization (QAOA) on the local simulator
match GraphColoring.solve problem 4 None with
| Ok solution ->
    printfn "Colors used: %d" solution.ColorsUsed
    solution.Assignments 
    |> Map.iter (fun node color -> printfn "%s → %s" node color)
| Error err -> 
    printfn "Error: %s" err.Message
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

**QAOA-based builders for common combinatorial problems:**

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

**Optional routing layer for variable-sized problems (TSP, Portfolio, MaxCut, Knapsack, Graph Coloring):**

- ✅ **Analyzes problem size** - `QuantumAdvisor` recommends classical below 20 variables and only strongly recommends quantum from 50 variables up (`QuantumAdvisor.defaultThresholds`)
- ✅ **Quantum only when asked for** - Routes to QAOA when the advisor strongly recommends quantum *and* you pass a backend (the `solve*WithBackend` functions), or when you force it with `Some HybridSolver.Quantum`; otherwise it runs the classical solver
- ✅ **Cost guards** - An optional budget (USD) sends the problem to the classical solver when the estimated quantum cost exceeds it
- ✅ **Transparent reasoning** - Every `HybridSolver.Solution` carries `Method` and a `Reasoning` string

**Recommendation:** Use the direct quantum API (`GraphColoring.solve`, `MaxCut.solve`, etc.) when you want QAOA. Use HybridSolver when you want a classical answer for small problems and quantum only for large ones.

**See:** [Getting Started Guide](getting-started) for detailed examples and decision criteria

### 🔬 QAOA Implementation

Quantum Approximate Optimization Algorithm with:
- ✅ Automatic QUBO encoding
- ✅ Gradient-free parameter optimization (Nelder–Mead) with multi-start, layer-by-layer and adaptive strategies (`QaoaParameterOptimizer`)
- ✅ Configurable circuit depth and shot counts
- ✅ Solution validation and quality metrics
- ✅ Integer variable support

**Example:** `examples/Optimization/QaoaParameterOptimizationExample.fsx`

### 🖥️ Multiple Execution Backends

- **LocalBackend** - State-vector simulation (width derived from available memory, hard ceiling 30 qubits; free)
- **IonQ** (`CloudBackends.IonQCloudBackend`) - Azure Quantum, trapped-ion (library limits: Aria 25, Forte 36 qubits)
- **Rigetti** (`CloudBackends.RigettiCloudBackend`) - Azure Quantum, superconducting (QPU 84 qubits)
- **Atom Computing** (`CloudBackends.AtomComputingCloudBackend`) - Azure Quantum, neutral atoms (QPU 100 qubits)
- **Quantinuum** (`CloudBackends.QuantinuumCloudBackend`) - Azure Quantum, trapped-ion (H1 32, H2 56 qubits)
- **IQM** (`CloudBackends.IqmCloudBackend`) - Azure Quantum, superconducting
- **D-Wave** (`DWaveBackend`, `RealDWaveBackend`) - Quantum annealer for QUBO problems (a mock annealer is included for local runs)
- **AWS Braket** - Separate `FSharp.Azure.Quantum.Braket` package (gate QPUs and simulators via OpenQASM 3.0, QuEra Aquila via AHS)
- **Topological** - Separate `FSharp.Azure.Quantum.Topological` package (anyon braiding simulator)

The provider simulators are capped at a conservative 20 qubits by the library.

### 🧭 Intent-First Algorithms

Some algorithms in this library are implemented as **intent → plan → execute** rather than as a fixed gate circuit. This allows the same algorithm to run on:

- gate-native backends (state-vector simulation, common providers), and
- non-gate-native backends (e.g., topological / Majorana-style models).

This is mostly transparent to users: you call the same API, but the backend may choose a different execution strategy. See the [Intent-First Algorithms ADR](adr-intent-first-algorithms).

### 💻 Cross-Language Support

- **F# First** - Idiomatic computation expressions and type safety
- **C# Friendly** - Fluent API extensions with value tuples
- **Seamless Interop** - Works naturally in both languages

## 🎯 Problem Types & Examples

### Graph Coloring

```fsharp
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.GraphColoring

// Time-slot assignment: tasks that conflict cannot share a slot
let problem = graphColoring {
    node "Task1" ["Task2"; "Task3"]
    node "Task2" ["Task1"; "Task4"]
    node "Task3" ["Task1"; "Task4"]
    node "Task4" ["Task2"; "Task3"]
    colors ["Slot A"; "Slot B"; "Slot C"]
    objective MinimizeColors
}

match GraphColoring.solve problem 3 None with
| Ok solution ->
    printfn "Valid coloring: %b" solution.IsValid
    printfn "Colors used: %d/%d" solution.ColorsUsed 3
    printfn "Conflicts: %d" solution.ConflictCount
| Error err -> printfn "Error: %s" err.Message
```

### MaxCut

```fsharp
let vertices = ["A"; "B"; "C"; "D"]
let edges = [
    ("A", "B", 1.0)
    ("B", "C", 2.0)
    ("C", "D", 1.0)
    ("D", "A", 1.0)
]

let problem = MaxCut.createProblem vertices edges

match MaxCut.solve problem None with
| Ok solution ->
    printfn "Partition S: %A" solution.PartitionS
    printfn "Partition T: %A" solution.PartitionT
    printfn "Cut value: %.2f" solution.CutValue
| Error err -> printfn "Error: %s" err.Message
```

### Knapsack

```fsharp
let items = [
    ("laptop", 3.0, 1000.0)   // (id, weight, value)
    ("phone", 0.5, 500.0)
    ("tablet", 1.5, 700.0)
    ("monitor", 2.0, 600.0)
]

let problem = Knapsack.createProblem items 5.0  // capacity = 5.0

match Knapsack.solve problem None with
| Ok solution ->
    printfn "Total value: $%.2f" solution.TotalValue
    printfn "Total weight: %.2f/%.2f" solution.TotalWeight problem.Capacity
    printfn "Items: %A" (solution.SelectedItems |> List.map (fun i -> i.Id))
| Error err -> printfn "Error: %s" err.Message
```

### TSP

```fsharp
let cities = [
    ("Seattle", 0.0, 0.0)
    ("Portland", 1.0, 0.5)
    ("San Francisco", 2.0, 1.5)
    ("Los Angeles", 3.0, 3.0)
]

let problem = TSP.createProblem cities

match TSP.solve problem None with
| Ok tour ->
    printfn "Optimal route: %s" (String.concat " → " tour.Cities)
    printfn "Total distance: %.2f" tour.TotalDistance
| Error err -> printfn "Error: %s" err.Message
```

### Portfolio

```fsharp
let assets = [
    ("AAPL", 0.12, 0.15, 150.0)  // (symbol, return, risk, price)
    ("GOOGL", 0.10, 0.12, 2800.0)
    ("MSFT", 0.11, 0.14, 350.0)
]

let problem = Portfolio.createProblem assets 10000.0  // budget

match Portfolio.solve problem None with
| Ok allocation ->
    printfn "Portfolio value: $%.2f" allocation.TotalValue
    printfn "Expected return: %.2f%%" (allocation.ExpectedReturn * 100.0)
    printfn "Risk: %.2f" allocation.Risk
    
    allocation.Allocations 
    |> List.iter (fun (symbol, shares, value) ->
        printfn "  %s: %.2f shares ($%.2f)" symbol shares value)
| Error err -> printfn "Error: %s" err.Message
```

### Network Flow

```fsharp
let nodes = [
    NetworkFlow.createSource "Factory" 100 100      // id, supply, capacity
    NetworkFlow.createIntermediate "Warehouse" 80   // id, capacity
    NetworkFlow.createSink "Store1" 40              // id, demand
    NetworkFlow.createSink "Store2" 60
]

let routes = [
    NetworkFlow.createRoute "Factory" "Warehouse" 5.0   // from, to, cost
    NetworkFlow.createRoute "Warehouse" "Store1" 3.0
    NetworkFlow.createRoute "Warehouse" "Store2" 4.0
]

let problem = NetworkFlow.createProblem nodes routes

match NetworkFlow.solve problem None with
| Ok flow ->
    printfn "Total cost: $%.2f" flow.TotalCost
    printfn "Fill rate: %.1f%%" (flow.FillRate * 100.0)
| Error err -> printfn "Error: %s" err.Message
```

### Task Scheduling

```fsharp
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.TaskScheduling
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Backends.LocalBackend

// Define tasks with dependencies (the type parameter is an optional payload; unit = none)
let taskA: ScheduledTask<unit> = scheduledTask {
    taskId "TaskA"
    duration (hours 2.0)
    priority 10.0
}

let taskB: ScheduledTask<unit> = scheduledTask {
    taskId "TaskB"
    duration (hours 1.5)
    after "TaskA"  // Dependency
    requires "Worker" 2.0
    deadline (hours 5.0)
}

let taskC: ScheduledTask<unit> = scheduledTask {
    taskId "TaskC"
    duration (minutes 30.0)
    after "TaskA"
    requires "Machine" 1.0
}

// Define resources
let worker: Resource<unit> = resource {
    resourceId "Worker"
    capacity 3.0
}

let machine: Resource<unit> = resource {
    resourceId "Machine"
    capacity 2.0
}

// Build scheduling problem
let problem = scheduling {
    tasks [taskA; taskB; taskC]
    resources [worker; machine]
    objective MinimizeMakespan
    timeHorizon (hours 8.0)
}

// Solve with a quantum backend (solveQuantum returns Async)
let backend = LocalBackend() :> IQuantumBackend
match solveQuantum backend problem |> Async.RunSynchronously with
| Ok solution ->
    printfn "Makespan: %.2f hours" solution.Makespan.TotalHours
    solution.Assignments 
    |> List.iter (fun assignment ->
        printfn "%s: starts %.2f h, ends %.2f h" 
            assignment.TaskId assignment.StartTime.TotalHours assignment.EndTime.TotalHours)
| Error err -> printfn "Error: %s" err.Message
```

## 🏗️ Architecture

**3-Layer Quantum-First Design:**

![3-Layer Quantum Architecture](images/3-layer-architecture.svg)

**Design Philosophy:**
- ✅ **Quantum-First**: The problem builders (`GraphColoring.solve`, `MaxCut.solve`, ...) run QAOA on a quantum backend and never fall back to a classical solver silently. Classical solvers exist only where you ask for them: `HybridSolver` routing and the `Classical` solvers (`TspSolver`, `PortfolioSolver`)
- ✅ **Clear Layers**: Business builders → quantum solvers → `IQuantumBackend`
- ✅ **Type-Safe**: F# type system prevents invalid problem specifications
- ✅ **Extensible**: Easy to add new problem types following existing patterns

## 📚 Complete Documentation

### 🚀 Getting Started
- [Getting Started Guide](getting-started) - Installation, first steps, and basic examples
- [Quantum Computing Introduction](quantum-computing-introduction) - Comprehensive introduction to quantum computing for F# developers (no quantum background needed)
- [Glossary](glossary) - Short plain-language definitions of every quantum term used in these docs
- [Mathematical Foundations](Mathematical-Foundations) - Quick reference for the math (complex numbers, vectors, matrices)
- [API Reference](api-reference) - Includes C# interop examples with fluent API

### 📖 Core Concepts
- [API Reference](api-reference) - Complete API documentation for all modules
- [Computation Expressions Reference](computation-expressions-reference) - Complete CE reference table with all custom operations (when IntelliSense fails)
- [Architecture Overview](architecture-overview) - Deep dive into 3-layer quantum-only design
- [Backend Switching](backend-switching) - Local vs Cloud vs D-Wave quantum execution
- [Hardware Selection Guide](Hardware-Selection-Guide) - Choosing the right quantum backend for your application
- [Local Simulation](local-simulation) - LocalBackend internals and performance characteristics
- [QuantumResult Builder Guide](quantumresult-builder-guide) - The `quantumResult` computation expression for clean error handling

### 🔬 Advanced Topics
- [QUBO Encoding Strategies](qubo-encoding-strategies) - Problem-to-QUBO transformations for QAOA
- [Computation Expression Composition](computation-expression-composition) - Advanced CE patterns for loops and composition
- [Topological Quantum Computing](topological/) - Fault-tolerant quantum computing with anyons and braiding
- [Topological Program Format Specification](topological-format-spec) - Draft serialization format for topological quantum programs
- [Intent-First Algorithms (ADR)](adr-intent-first-algorithms) - Why some algorithms plan per backend instead of fixing a gate circuit
- [Quantum Machine Learning](quantum-machine-learning) - VQC, Quantum Kernels, Feature Maps
- [Business Problem Builders](business-problem-builders) - AutoML, Fraud Detection, Anomaly Detection, Predictive Modeling
- [Error Mitigation](error-mitigation) - ZNE, PEC, REM strategies for NISQ hardware
- [Bring Your Own Hamiltonian](bring-your-own-hamiltonian) - Plug in external chemistry packages (PySCF, Psi4, FCIDUMP, OpenFermion-style operators)
- [Advanced Quantum Builders](advanced-quantum-builders) - Tree Search, Constraint Solver, Pattern Matcher, Shor's Algorithm, Phase Estimation
- D-Wave Integration Guide - not yet written; see [Backend Switching](backend-switching) for current D-Wave usage
- [FAQ](faq) - Frequently asked questions and troubleshooting

### 🎯 Problem-Specific API Guides
- [Graph Coloring API](GraphColoring-API) - Register allocation, frequency assignment, scheduling
- [Task Scheduling API](TaskScheduling-API) - Constraint-based quantum scheduling
- [QRNG API](QRNG-API) - Quantum random number generation

> Other algorithm APIs (HHL, Trotter–Suzuki, Quantum Monte Carlo, teleportation,
> quantum & statistical distributions, quantum chemistry) are documented as
> **runnable example scripts** — see *Working Code Examples* below. A compiling
> `.fsx` is self-verifying and stays current; prose API docs drift out of date.

### 💡 Working Code Examples

**View source code on GitHub:**

#### Optimization Problems (QAOA)
- [**DeliveryRouting**](https://github.com/Thorium/FSharp.Azure.Quantum/tree/main/examples/DeliveryRouting) - TSP with 16-city NYC routing, HybridSolver
- [**InvestmentPortfolio**](https://github.com/Thorium/FSharp.Azure.Quantum/tree/main/examples/InvestmentPortfolio) - Portfolio optimization with constraints (F#)
- [**InvestmentPortfolio_CSharp**](https://github.com/Thorium/FSharp.Azure.Quantum/tree/main/examples/InvestmentPortfolio/CSharp) - Portfolio optimization (C# version)
- [**GraphColoring**](https://github.com/Thorium/FSharp.Azure.Quantum/tree/main/examples/GraphColoring) - Graph coloring with QAOA
- [**MaxCut**](https://github.com/Thorium/FSharp.Azure.Quantum/tree/main/examples/MaxCut) - Max-Cut problem with QAOA
- [**Knapsack**](https://github.com/Thorium/FSharp.Azure.Quantum/tree/main/examples/Knapsack) - 0/1 Knapsack optimization
- [**SupplyChain**](https://github.com/Thorium/FSharp.Azure.Quantum/tree/main/examples/SupplyChain) - Multi-constraint resource allocation
- [**JobScheduling**](https://github.com/Thorium/FSharp.Azure.Quantum/tree/main/examples/JobScheduling) - Task scheduling with dependencies

#### Advanced Quantum Algorithms
- [**QuantumChemistry**](https://github.com/Thorium/FSharp.Azure.Quantum/tree/main/examples/Chemistry) - VQE for molecular simulation (verified H2/STO-3G integrals)
- [**PhaseEstimation**](https://github.com/Thorium/FSharp.Azure.Quantum/tree/main/examples/PhaseEstimation) - Quantum Phase Estimation (QPE)
- [**QuantumArithmetic**](https://github.com/Thorium/FSharp.Azure.Quantum/tree/main/examples/QuantumArithmetic) - QFT-based arithmetic operations
- [**CryptographicAnalysis**](https://github.com/Thorium/FSharp.Azure.Quantum/tree/main/examples/CryptographicAnalysis) - Shor's algorithm demonstrations
- [**HHL (Linear Systems)**](https://github.com/Thorium/FSharp.Azure.Quantum/tree/main/examples/LinearSystemSolver) - Harrow–Hassidim–Lloyd solver
- [**QuantumTeleportation**](https://github.com/Thorium/FSharp.Azure.Quantum/blob/main/examples/Protocols/QuantumTeleportationExample.fsx) - State teleportation protocol
- [**QuantumDistributions**](https://github.com/Thorium/FSharp.Azure.Quantum/tree/main/examples/QuantumDistributions) - QRNG-based probability sampling
- [**Financial Risk (Quantum Monte Carlo)**](https://github.com/Thorium/FSharp.Azure.Quantum/tree/main/examples/FinancialRisk) - Option pricing, VaR & stress testing via amplitude estimation
- [**Trotter–Suzuki**](https://github.com/Thorium/FSharp.Azure.Quantum/blob/main/examples/Algorithms/TrotterSuzukiExample.fsx) - Hamiltonian time-evolution decomposition
- [**Statistical Distributions**](https://github.com/Thorium/FSharp.Azure.Quantum/blob/main/examples/Algorithms/StatisticalDistributionsExample.fsx) - Numerical PDF / CDF / quantile helpers

#### Interactive Demonstrations
- [**Gomoku**](https://github.com/Thorium/FSharp.Azure.Quantum/tree/main/examples/Gomoku) - Quantum vs Classical AI game (with Hybrid mode)
- [**Kasino**](https://github.com/Thorium/FSharp.Azure.Quantum/tree/main/examples/Kasino) - Quantum gambling game demonstrating superposition (F#)
- [**Kasino_CSharp**](https://github.com/Thorium/FSharp.Azure.Quantum/tree/main/examples/Kasino/CSharp) - Quantum gambling game (C# version)

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
- **Use HybridSolver** if you want a classical answer for small problems and quantum only when the advisor strongly recommends it (50+ variables by default) and you supply a backend
- **LocalBackend (default)** provides free quantum simulation at a width derived from available memory (hard ceiling 30 qubits) - suited to development, testing and small problems
- **Cloud backends** (IonQ, Rigetti, Quantinuum, Atom Computing, IQM) for real quantum hardware experimentation

## 🔧 Backend Selection Guide

### LocalBackend (Default)

```fsharp
let maxCutProblem = MaxCut.createProblem vertices edges   // from the MaxCut example above

// Automatic: No backend parameter needed
match MaxCut.solve maxCutProblem None with
| Ok solution -> printfn "Max cut value: %.2f" solution.CutValue
| Error err -> printfn "Error: %s" err.Message
```

**Characteristics:**
- ✅ Free (local simulation)
- ✅ Fast for small circuits (milliseconds per gate up to about 20 qubits)
- ✅ Up to `StateVector.maxQubits` (derived from available memory; hard ceiling 30; override with `FSAQ_MAX_QUBITS`)
- ✅ Suited to development and testing

### Azure Quantum (Cloud)

Cloud backends talk to an Azure Quantum workspace over HTTP. Authenticate with an Azure credential (e.g. after `az login`) and pass the workspace URL:

```fsharp
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Backends.CloudBackends

// https://<location>.quantum.azure.com/subscriptions/<sub>/resourceGroups/<rg>/providers/Microsoft.Quantum/workspaces/<ws>
let workspaceUrl = "https://eastus.quantum.azure.com/subscriptions/..."

let credential = Authentication.CredentialProviders.createDefaultCredential ()
let httpClient = Authentication.createAuthenticatedClient credential

// httpClient, workspace URL, target, shots
let backend_ionq = CloudBackendFactory.createIonQ httpClient workspaceUrl "ionq.simulator" 1000
let backend_rigetti = CloudBackendFactory.createRigetti httpClient workspaceUrl "rigetti.sim.qvm" 1000
let backend_atom = CloudBackendFactory.createAtomComputing httpClient workspaceUrl "atom-computing.sim" 1000
let backend_quantinuum = CloudBackendFactory.createQuantinuum httpClient workspaceUrl "quantinuum.sim.h1-1sc" 1000

// Pass to solver
match MaxCut.solve maxCutProblem (Some backend_ionq) with
| Ok solution -> printfn "Max cut value: %.2f" solution.CutValue
| Error err -> printfn "Error: %s" err.Message
```

`FSharp.Azure.Quantum.Backends.AzureQuantumWorkspace` (`createDefault`, `createFromEnvironment`) is for workspace management such as quota and provider queries; it does not create execution backends.

**Backend Characteristics** (the qubit limits the library enforces):

| Backend | Qubits | Technology |
|---------|--------|------------|
| **IonQ** | Aria 25, Forte 36 | Trapped-ion |
| **Rigetti** | QPU 84 | Superconducting |
| **Atom Computing** | QPU 100 | Neutral atoms |
| **Quantinuum** | H1 32, H2 56 | Trapped-ion |
| **IQM** | QPU 20 | Superconducting |

Provider simulator targets are capped at a conservative 20 qubits.

**Cost & Performance:**
- ⚡ Real quantum hardware available
- 💰 Paid service; cost varies by provider and shot count
- ⏱️ Slower (job queue and polling; seconds to minutes per job)

## 🤝 Contributing

Contributions welcome! See [GitHub Repository](https://github.com/thorium/FSharp.Azure.Quantum) for contribution guidelines.

**Areas we'd love help with:**
- New problem builders (Job Shop Scheduling, Vehicle Routing)
- QAOA warm-start strategies
- Additional cloud backend support (e.g. IBM Quantum)
- Performance optimizations

## 🔗 Links

- [GitHub Repository](https://github.com/thorium/FSharp.Azure.Quantum)
- [NuGet Package](https://www.nuget.org/packages/FSharp.Azure.Quantum/)
- [Report Issues](https://github.com/thorium/FSharp.Azure.Quantum/issues)
- [API Documentation](api-reference)

## 📊 Performance Guidelines

The number of qubits a QAOA run needs depends on the encoding:

| Problem Type | Qubits needed | About 20 qubits means |
|--------------|---------------|-----------------------|
| Graph Coloring | nodes × colors | 6 nodes with 3 colors |
| MaxCut | one per vertex | 20 vertices |
| Knapsack | one per item | 20 items |
| TSP | cities² | 4 cities |
| Portfolio | one per asset | 20 assets |
| Network Flow | one per route | 20 routes |
| Task Scheduling | tasks × time slots | 4 tasks × 5 slots |

**Note:** LocalBackend's width is derived from available memory (hard ceiling 30 qubits); see `StateVector.maxQubits`. Each extra qubit doubles the time per gate, so iterative algorithms such as QAOA are practical up to about 20 qubits (`StateVector.practicalCircuitQubits`). Larger problems need cloud backends or a smaller encoding.

## 📄 License

This project is licensed under the [Unlicense](https://unlicense.org/) - dedicated to the public domain.

---

**Status**: Quantum-first architecture with 7 optimization problem builders (package version 1.4.12)

**Last Updated**: 2026-09-29
