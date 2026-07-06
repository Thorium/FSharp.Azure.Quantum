# FSharp.Azure.Quantum

**Quantum-First F# Library** - Solve combinatorial optimization problems using quantum algorithms (QAOA - Quantum Approximate Optimization Algorithm) with automatic cloud/local backend selection.

[![NuGet](https://img.shields.io/nuget/v/FSharp.Azure.Quantum.svg)](https://www.nuget.org/packages/FSharp.Azure.Quantum/)
[![License](https://img.shields.io/badge/license-Unlicense-blue.svg)](LICENSE)

```mermaid
flowchart TD

    %% Top layer
    A[.NET API High-level algorithms familiar to normal developers]

    %% Quantum algorithms
    B[Quantum Algorithms, 
       Ph.D stuff]

    %% Backend interfaces
    C[Backend Interfaces: IQuantumBackend]

    %% Quantum Gate Backends
    D[Quantum Gate Backends: 
        LocalSimulator: Ph.D stuff, 
        Azure Quantum as a service]


    %% Topological Backends
    E[Topological Backend simulator, 
       more Ph.D stuff]

    %% Flow
    A --> B --> C
    C --> D
    C --> E
```

## Table of Contents

- [Status](#status)
- [Quick Start](#quick-start)
  - [Installation](#installation)
  - [F# Computation Expressions](#f-computation-expressions)
  - [C# Fluent API](#c-fluent-api)
- [Problem Builders](#problem-builders)
  - [Graph Coloring](#graph-coloring)
  - [MaxCut](#maxcut)
  - [Knapsack (0/1)](#knapsack-01)
  - [Traveling Salesperson Problem (TSP)](#traveling-salesperson-problem-tsp)
  - [Portfolio Optimization](#portfolio-optimization)
  - [Network Flow](#network-flow)
  - [Task Scheduling](#task-scheduling)
- [Advanced Quantum Builders](#advanced-quantum-builders)
  - [Quantum Tree Search Builder](#quantum-tree-search-builder)
  - [Quantum Constraint Solver Builder](#quantum-constraint-solver-builder)
  - [Quantum Pattern Matcher Builder](#quantum-pattern-matcher-builder)
  - [Quantum Arithmetic Builder](#quantum-arithmetic-builder)
  - [Quantum Period Finder Builder](#quantum-period-finder-builder)
  - [Quantum Phase Estimator Builder](#quantum-phase-estimator-builder)
- [Quantum Machine Learning (QML)](#quantum-machine-learning-qml)
  - [Variational Quantum Classifier (VQC)](#variational-quantum-classifier-vqc)
  - [Quantum Kernel SVM](#quantum-kernel-svm)
- [Business Problem Builders](#business-problem-builders)
  - [Social Network Analyzer](#social-network-analyzer---community-detection--fraud-rings)
  - [Constraint Scheduler](#constraint-scheduler---workforce--resource-allocation)
  - [AutoML](#automl---automated-machine-learning)
  - [Anomaly Detection](#anomaly-detection---security--fraud-detection)
  - [Binary Classification](#binary-classification---fraud-detection)
  - [Predictive Modeling](#predictive-modeling---customer-churn-prediction)
  - [Similarity Search](#similarity-search---product-recommendations)
- [HybridSolver](#hybridsolver---automatic-classicalquantum-routing)
- [Architecture](#architecture)
  - [3-Layer Quantum-Only Architecture](#3-layer-quantum-only-architecture)
  - [D-Wave Quantum Annealer](#d-wave-quantum-annealer)
  - [Azure Quantum Workspace Management](#azure-quantum-workspace-management)
- [OpenQASM 2.0 Support](#openqasm-20-support)
- [Error Mitigation](#error-mitigation)
  - [Zero-Noise Extrapolation (ZNE)](#1%EF%B8%8F⃣-zero-noise-extrapolation-zne)
  - [Probabilistic Error Cancellation (PEC)](#2%EF%B8%8F⃣-probabilistic-error-cancellation-pec)
  - [Readout Error Mitigation (REM)](#3%EF%B8%8F⃣-readout-error-mitigation-rem)
  - [Automatic Strategy Selection](#4%EF%B8%8F⃣-automatic-strategy-selection)
- [QAOA Algorithm Internals](#qaoa-algorithm-internals)
- [Documentation](#documentation)
- [Problem Size Guidelines](#problem-size-guidelines)
- [Design Philosophy](#design-philosophy)
- [Quantum Algorithms](#quantum-algorithms)
  - [Grover's Search Algorithm](#grovers-search-algorithm)
  - [Amplitude Amplification](#amplitude-amplification)
  - [Quantum Fourier Transform (QFT)](#quantum-fourier-transform-qft)
- [Topological Quantum Computing](#topological-quantum-computing)
- [Contributing](#contributing)
- [License](#license)
- [Support](#support)

## Status

**Architecture:** Quantum-First Hybrid Library - Quantum algorithms as primary solvers, with opt-in classical routing (via `HybridSolver` / `QuantumAdvisor`) for small problems where quantum offers no advantage. Quantum solvers never fall back to classical silently — see [Design Philosophy](#design-philosophy).

**Current Version:** 1.4.2 (core) / 0.4.2 (Topological plugin) — D-Wave Support + Quantum Machine Learning + Business Builders + compilation/hardware tooling (QIR, resource estimation, qubit routing, noise-aware routing)

**Current Features:**
- Multiple Backends: LocalBackend (simulation), Azure Quantum (IonQ, Rigetti, Atom Computing, Quantinuum), D-Wave quantum annealers (2000+ qubits)
- Topological Quantum Computing: Anyon braiding simulator (Ising, Fibonacci & SU(2)_k anyons) with error correction (toric code, surface codes, anyonic charge correction) - Microsoft Majorana architecture
- Quantum Machine Learning: VQC, Quantum Kernel SVM, Feature Maps, Variational Forms, AutoML
- Business Problem Builders: Social Network Analysis, Constraint Scheduling, AutoML, Anomaly Detection, Binary Classification, Predictive Modeling, Similarity Search
- OpenQASM 2.0: Import/export compatibility with IBM Qiskit, Amazon Braket, Google Cirq
- QAOA Implementation: Quantum Approximate Optimization Algorithm with advanced parameter optimization
- 7 Quantum Optimization Builders: Graph Coloring, MaxCut, Knapsack, TSP, Portfolio, Network Flow, Task Scheduling
- 6 Advanced QFT-Based Builders: Quantum Arithmetic, Cryptographic Analysis (Shor's), Phase Estimation, Tree Search, Constraint Solver, Pattern Matcher
- VQE Implementation: Variational Quantum Eigensolver for molecular ground state energies (quantum chemistry)
- Error Mitigation: ZNE (30-50% error reduction), PEC (2-3x accuracy), REM (50-90% readout correction)
- F# Computation Expressions: Idiomatic, type-safe problem specification with builders
- C# Interop: Fluent API extensions for C# developers
- Circuit Building: Low-level quantum circuit construction and optimization possible

---

## Quick Start

### Installation

```bash
dotnet add package FSharp.Azure.Quantum
```

### F# Computation Expressions

```fsharp
open FSharp.Azure.Quantum

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

**What happens:**
1. Computation expression builds graph coloring problem
2. `GraphColoring.solve` calls `QuantumGraphColoringSolver` internally
3. QAOA quantum algorithm encodes problem as QUBO (Quadratic Unconstrained Binary Optimization)
4. LocalBackend simulates quantum circuit (≤20 qubits)
5. Returns color assignments with validation

---

## Problem Builders

**Optimization Builders using QAOA:**

### Graph Coloring

**Use Case:** Register allocation, frequency assignment, exam scheduling

```fsharp
open FSharp.Azure.Quantum

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
| Error msg -> printfn "Error: %s" msg
```

### MaxCut

**Use Case:** Circuit design, community detection, load balancing

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
| Error msg -> printfn "Error: %s" msg
```

### Knapsack (0/1)

**Use Case:** Resource allocation, portfolio selection, cargo loading

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
| Error msg -> printfn "Error: %s" msg
```

### Traveling Salesperson Problem (TSP)

**Use Case:** Route optimization, delivery planning, logistics

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
| Error msg -> printfn "Error: %s" msg
```

### Portfolio Optimization

**Use Case:** Investment allocation, asset selection, risk management

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
| Error msg -> printfn "Error: %s" msg
```

### Network Flow

**Use Case:** Supply chain optimization, logistics, distribution planning

```fsharp
open FSharp.Azure.Quantum

let nodes = [
    NetworkFlow.createSource "Factory" 100 100   // id, supply, capacity
    NetworkFlow.createIntermediate "Warehouse" 80
    NetworkFlow.createSink "Store1" 40           // id, demand
    NetworkFlow.createSink "Store2" 60
]

let routes = [
    NetworkFlow.createRoute "Factory" "Warehouse" 5.0   // from, to, cost
    NetworkFlow.createRoute "Warehouse" "Store1" 3.0
    NetworkFlow.createRoute "Warehouse" "Store2" 4.0
]

let problem = NetworkFlow.createProblem nodes routes

// Pass Some backend to run on quantum hardware/simulator; None uses the default.
match NetworkFlow.solve problem None with
| Ok flow ->
    printfn "Total cost: $%.2f" flow.TotalCost
    printfn "Fill rate: %.1f%%" (flow.FillRate * 100.0)
| Error err -> printfn "Error: %A" err
```

### Task Scheduling

**Use Case:** Manufacturing workflows, project management, resource allocation with dependencies

```fsharp
open FSharp.Azure.Quantum

// Define tasks with dependencies
let taskA = scheduledTask {
    taskId "TaskA"
    duration (hours 2.0)
    priority 10.0
}

let taskB = scheduledTask {
    taskId "TaskB"
    duration (hours 1.5)
    after "TaskA"  // Dependency
    requires "Worker" 2.0
    deadline 180.0
}

let taskC = scheduledTask {
    taskId "TaskC"
    duration (minutes 30.0)
    after "TaskA"
    requires "Machine" 1.0
}

// Define resources
let worker = resource {
    resourceId "Worker"
    capacity 3.0
}

let machine = resource {
    resourceId "Machine"
    capacity 2.0
}

// Build scheduling problem
let problem = scheduling {
    tasks [taskA; taskB; taskC]
    resources [worker; machine]
    objective MinimizeMakespan
    timeHorizon 500.0
}

// Solve with quantum backend for resource constraints
let backend = LocalBackend() :> IQuantumBackend
match solveQuantum backend problem with
| Ok solution ->
    printfn "Makespan: %.2f hours" solution.Makespan
    solution.Schedule 
    |> List.iter (fun assignment ->
        printfn "%s: starts %.2f, ends %.2f" 
            assignment.TaskId assignment.StartTime assignment.EndTime)
| Error msg -> printfn "Error: %s" msg
```

**Features:**
- Dependency Management - Precedence constraints (task A before task B)
- Resource Constraints - Limited workers, machines, budget
- Quantum Optimization - QAOA for resource-constrained scheduling
- Classical Fallback - Topological sort for dependency-only problems
- Gantt Chart Export - Visualize schedules
- Business ROI - Validated $25,000/hour savings in powerplant optimization

**API Documentation:** [TaskScheduling-API.md](docs/TaskScheduling-API.md)

**Examples:** 
- [JobScheduling](examples/JobScheduling/) - Manufacturing workflow scheduling
- [ProjectManagement](examples/ProjectManagement/) - Team task allocation (if exists)

---

## Advanced Quantum Builders

**High-level builders for specialized quantum algorithms using computation expression syntax.**

Beyond the optimization and QFT-based builders, the library provides five advanced builders for specialized quantum computing tasks:

### Quantum Tree Search Builder

**Use Case:** Graph traversal, decision trees, game tree exploration

```fsharp
open FSharp.Azure.Quantum

// Search a decision tree with quantum parallelism
let searchProblem = QuantumTreeSearch.quantumTreeSearch {
    initialState [0]                                        // Root state
    maxDepth 5                                              // Tree depth
    branchingFactor 3                                       // Children per node
    evaluateWith (fun state -> float (List.sum state))     // Score a state
    generateMovesWith (fun state -> [ for c in 1..3 -> state @ [c] ])  // Expand a node
}

match QuantumTreeSearch.solve searchProblem with
| Ok result ->
    printfn "Best move: %d" result.BestMove
    printfn "Score: %.4f" result.Score
    printfn "Paths explored: %d" result.PathsExplored
    printfn "Quantum advantage: %b" result.QuantumAdvantage
| Error err ->
    printfn "Error: %A" err
```

**Features:**
- Quantum parallelism for tree exploration
- Amplitude amplification for target finding
- F# computation expression: `QuantumTreeSearch.quantumTreeSearch { }`
- Applications: Game AI, route planning, decision analysis

---

### Quantum Constraint Solver Builder

**Use Case:** SAT solving, constraint satisfaction problems, logic puzzles

```fsharp
open FSharp.Azure.Quantum

// Solve a constraint-satisfaction problem (here: a 3-variable "all different")
let satProblem = QuantumConstraintSolver.constraintSolver {
    searchSpace 3                          // Number of variables (indices 0..2)
    domain [0; 1; 2]                       // Value each variable may take
    satisfies (fun assignment ->           // True when all constraints hold
        assignment.[0] <> assignment.[1] &&
        assignment.[1] <> assignment.[2] &&
        assignment.[0] <> assignment.[2])
}

match QuantumConstraintSolver.solve satProblem with
| Ok solution ->
    printfn "Satisfying assignment:"
    solution.Assignment
    |> Map.iter (fun var value -> printfn "  var%d = %A" var value)
    printfn "All constraints satisfied: %b" solution.AllConstraintsSatisfied
    printfn "Success probability: %.2f%%" (solution.SuccessProbability * 100.0)
| Error err ->
    printfn "Error: %A" err
```

**Features:**
- Grover-based SAT solving
- CNF (Conjunctive Normal Form) constraint encoding
- F# computation expression: `QuantumConstraintSolver.constraintSolver { }`
- Applications: Circuit verification, scheduling, logic puzzles

---

### Quantum Pattern Matcher Builder

**Use Case:** String matching, DNA sequence alignment, anomaly detection

```fsharp
open FSharp.Azure.Quantum

// Find the items matching a predicate, with quantum search speedup
let matchProblem = QuantumPatternMatcher.patternMatcher {
    searchSpace [1; 2; 3; 4; 5; 6; 7; 8]   // Candidate items
    matchPattern (fun x -> x % 3 = 0)      // Pattern: multiples of 3
    findTop 2                              // Return up to 2 matches
}

match QuantumPatternMatcher.solve matchProblem with
| Ok result ->
    printfn "Matches found: %A" result.Matches
    printfn "Search space size: %d" result.SearchSpaceSize
    printfn "Success probability: %.2f%%" (result.SuccessProbability * 100.0)
| Error err ->
    printfn "Error: %A" err
```

**Features:**
- Quantum search for pattern matching
- Approximate matching with tolerance
- F# computation expression: `QuantumPatternMatcher.patternMatcher { }`
- Applications: Bioinformatics, data mining, signal processing

---

### Quantum Arithmetic Builder

**Use Case:** Cryptographic operations, RSA encryption, modular arithmetic

```fsharp
open FSharp.Azure.Quantum

// Quantum integer addition: 15 + 27
let addProblem = QuantumArithmeticOps.quantumArithmetic {
    operands 15 27
    operation QuantumArithmeticOps.Add
    qubits 10
}

// The CE returns a validated Result<ArithmeticOperation, _>; bind, then execute.
match addProblem with
| Ok op ->
    match QuantumArithmeticOps.execute op with
    | Ok result ->
        printfn "Result: %d" result.Value
        printfn "Gates used: %d" result.GateCount
        printfn "Circuit depth: %d" result.CircuitDepth
        printfn "Modular: %b" result.IsModular
    | Error err -> printfn "Error: %A" err
| Error err -> printfn "Error: %A" err

// Modular exponentiation for RSA: 5^3 mod 33
let rsaProblem = QuantumArithmeticOps.quantumArithmetic {
    operands 5 3                                // base = 5, exponent = 3
    operation QuantumArithmeticOps.ModularExponentiate
    modulus 33                                  // RSA modulus
    qubits 12
}
```

**Features:**
- Quantum arithmetic operations (Add, Subtract, Multiply, ModularExponentiate)
- QFT-based carry propagation
- F# computation expression: `QuantumArithmeticOps.quantumArithmetic { }`
- Applications: Cryptography, financial calculations, scientific computing

---

### Quantum Period Finder Builder

**Use Case:** Shor's algorithm, cryptanalysis, order finding

```fsharp
open FSharp.Azure.Quantum

// Find the period of a modular function (core of Shor's algorithm)
let shorsProblem = QuantumPeriodFinder.periodFinder {
    number 15                   // Composite to factor
    chosenBase 7                // Coprime base
    precision 12                // QPE precision bits
    maxAttempts 10              // Probabilistic retries
}

// The CE returns a validated Result<PeriodFinderProblem, _>; bind, then solve.
match shorsProblem with
| Ok problem ->
    match QuantumPeriodFinder.solve problem with
    | Ok result ->
        printfn "Period found: %d" result.Period
        match result.Factors with
        | Some (p, q) ->
            printfn "Factors: %d x %d = %d" p q (p * q)
            printfn "RSA modulus factored!"
        | None ->
            printfn "Retry with a different base (probabilistic)"
    | Error err -> printfn "Error: %A" err
| Error err -> printfn "Error: %A" err
```

**Features:**
- Quantum Period Finding (QPF) for Shor's algorithm
- Quantum Phase Estimation (QPE) integration
- F# computation expression: `QuantumPeriodFinder.periodFinder { }`
- Applications: Cryptanalysis, number theory, discrete logarithm

---

### Quantum Phase Estimator Builder

**Use Case:** Eigenvalue estimation, quantum chemistry, VQE enhancement

```fsharp
open System
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.Algorithms.QPE   // UnitaryOperator (RotationZ, PhaseGate, ...)

// Estimate the eigenphase of a unitary operator
let qpeProblem = QuantumPhaseEstimator.phaseEstimator {
    unitary (RotationZ (Math.PI / 4.0))   // Operator U
    precision 16                          // 16-bit phase precision
    targetQubits 1
}

// The CE returns a validated Result<PhaseEstimatorProblem, _>; bind, then estimate.
match qpeProblem with
| Ok problem ->
    match QuantumPhaseEstimator.estimate problem with
    | Ok result ->
        printfn "Estimated phase: %.6f" result.Phase
        printfn "Eigenvalue: %A" result.Eigenvalue
        printfn "Measurement outcome: %d" result.MeasurementOutcome
        printfn "Molecular energy ~ %.4f Hartree" (result.Phase * 2.0 * Math.PI)
    | Error err -> printfn "Error: %A" err
| Error err -> printfn "Error: %A" err
```

**Features:**
- Quantum Phase Estimation (QPE) with arbitrary precision
- Inverse QFT for phase readout
- F# computation expression: `QuantumPhaseEstimator.phaseEstimator { }`
- Applications: Quantum chemistry (VQE), linear systems, machine learning

---

### Advanced Builder Features

**Common Capabilities:**
- Computation Expression Syntax - Idiomatic F# DSL for all builders
- Backend Switching - LocalBackend (simulation) or Cloud (IonQ, Rigetti)
- Type Safety - F# type system prevents invalid quantum circuits
- C# Interop - Fluent API extensions for all builders
- Circuit Export - Export to OpenQASM 2.0 for cross-platform execution
- Error Handling - Comprehensive validation and error messages

**Current Status:** 
- Educational/Research Focus - Suitable for algorithm learning and prototyping
- NISQ Limitations - Toy problems only (< 20 qubits) on current hardware
- Production-Quality Code - Well-tested, documented, and production-quality implementation
- Hardware Bottleneck - Waiting for fault-tolerant quantum computers for real-world scale

**Use Cases by Builder:**

| Builder | Primary Application | Quantum Advantage | Hardware Readiness |
|---------|-------------------|-------------------|-------------------|
| **Tree Search** | Game AI, Route Planning | O(√N) speedup | 2028+ (requires 50+ qubits) |
| **Constraint Solver** | SAT, Logic Puzzles | O(√2^n) speedup | 2030+ (requires 100+ qubits) |
| **Pattern Matcher** | DNA Alignment, Data Mining | O(√N) speedup | 2027+ (requires 30+ qubits) |
| **Arithmetic** | Cryptography, RSA | Polynomial vs exponential | 2026+ (requires 4096+ qubits for RSA-2048) |
| **Period Finder** | Shor's Algorithm, Cryptanalysis | Exponential speedup | 2029+ (requires 4096+ qubits) |
| **Phase Estimator** | Quantum Chemistry, VQE | Polynomial speedup | 2025+ (10-20 qubits sufficient for small molecules) |

**Recommendation:**
- Use for **learning quantum algorithms** and understanding quantum advantage
- Use for **prototyping** future quantum applications
- Use **Phase Estimator for small molecules** on current NISQ hardware
- For **production optimization**, use the [7 problem builders](#-problem-builders) instead

---

### C# API for Advanced Builders

All advanced builders have C#-friendly fluent APIs:

```csharp
using System;
using FSharp.Azure.Quantum;
using static FSharp.Azure.Quantum.CSharpBuilders;

// Tree Search: initial state, scoring function, and move generator
var treeSearch = QuantumTreeSearch(
    initialState: 0,
    evaluator: x => (double)x,
    moveGenerator: x => new[] { x + 1, x + 2 });
var searchResult = SolveTreeSearch(treeSearch);

// Constraint Solver: search-space size, domain, and a constraint predicate
var satProblem = QuantumConstraintSolver(
    searchSpaceSize: 3,
    domain: new[] { 0, 1 },
    singleConstraint: assignment => assignment[0] != assignment[1]);
var satResult = SolveConstraints(satProblem);

// Pattern Matcher: candidate configurations and a match predicate
var pattern = QuantumPatternMatcher(
    configurations: new[] { 1, 2, 3, 4, 5, 6, 7, 8 },
    pattern: x => x % 3 == 0);
var matchResult = SolvePatternMatch(pattern);

// Arithmetic: build an operation, then execute it
var add = Add(15, 27);
var addResult = ExecuteArithmetic(add);

var rsa = ModularExponentiate(baseValue: 5, exponent: 3, modulus: 33);
var rsaResult = ExecuteArithmetic(rsa);

// Period Finder (Shor's): the factory returns a Result; unwrap before executing
var shors = FactorInteger(15, precision: 12);
if (shors.IsOk)
{
    var factorResult = ExecutePeriodFinder(shors.ResultValue);
}

// Phase Estimator: EstimateRotationZ also returns a Result
var qpe = EstimateRotationZ(Math.PI / 4.0, precision: 16);
if (qpe.IsOk)
{
    var phaseResult = ExecutePhaseEstimator(qpe.ResultValue);
}
```

**Current Status:** Educational/research focus - Demonstrates quantum algorithms but hardware insufficient for real-world applications (as of 2025)

---

### Library Scope & Focus

**Primary Focus: QAOA-Based Combinatorial Optimization**

This library is designed for **NISQ-era practical quantum advantage** in optimization:
- ✅ 7 optimization problem builders (Graph Coloring, MaxCut, TSP, Knapsack, Portfolio, Network Flow, Task Scheduling)
- ✅ QAOA implementation with automatic parameter tuning
- ✅ Error mitigation for noisy hardware (ZNE, PEC, REM)
- ✅ Production-ready solvers with cloud backend integration

**Secondary Focus: Quantum Algorithm Education & Research**

The `Algorithms/` directory contains foundational quantum algorithms for learning:
- ✅ Grover's Search (quantum search, O(√N) speedup)
- ✅ Amplitude Amplification (generalization of Grover)
- ✅ Quantum Fourier Transform (O(n²) vs O(n·2^n) classical FFT)

**Why F# for Quantum?**
- Type-safe quantum circuit construction
- Functional programming matches quantum mathematics
- Interop with .NET ecosystem (C#, Azure, ML.NET)
- Higher level abstraction than Python (Qiskit) and Q#

---

## Quantum Machine Learning (QML)

**Apply quantum computing to machine learning problems with variational quantum circuits and quantum kernels.**

### Variational Quantum Classifier (VQC)

Train quantum neural networks for classification tasks:

```fsharp
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.MachineLearning

// Setup backend and architecture
let backend = LocalBackend() :> IQuantumBackend
let featureMap = AngleEncoding
let variationalForm = RealAmplitudes 2  // 2 layers

// Prepare training data (features and labels as separate arrays)
let trainFeatures = [|
    [| 0.1; 0.2 |]
    [| 0.9; 0.8 |]
    [| 0.3; 0.1 |]
    [| 0.8; 0.9 |]
|]
let trainLabels = [| 0; 1; 0; 1 |]

// Configure training
let config = {
    LearningRate = 0.1
    MaxEpochs = 100
    ConvergenceThreshold = 0.001
    Shots = 1000
    Verbose = false
    Optimizer = VQC.SGD
}

// Initialize parameters
let numQubits = trainFeatures.[0].Length
let numParams = AnsatzHelpers.parameterCount variationalForm numQubits
let initialParams = Array.init numParams (fun _ -> Random().NextDouble() * 2.0 * Math.PI)

// Train the classifier
match VQC.train backend featureMap variationalForm initialParams trainFeatures trainLabels config with
| Ok result ->
    // Make predictions
    let testPoint = [| 0.5; 0.5 |]
    match VQC.predict backend featureMap variationalForm result.Parameters testPoint 1000 with
    | Ok prediction ->
        printfn "Prediction: %d (probability: %.2f%%)" 
            prediction.Label (prediction.Probability * 100.0)
    | Error msg -> printfn "Error: %s" msg
| Error msg -> printfn "Training failed: %s" msg
```

### Quantum Kernel SVM

Use quantum feature spaces for support vector machines:

```fsharp
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.MachineLearning

// Setup backend and quantum feature map
let backend = LocalBackend() :> IQuantumBackend
let featureMap = ZZFeatureMap  // Quantum feature map with entanglement

// Training data
let trainData = [| [| 0.1; 0.2 |]; [| 0.9; 0.8 |]; [| 0.3; 0.1 |]; [| 0.8; 0.9 |] |]
let trainLabels = [| 0; 1; 0; 1 |]

// SVM configuration
let config = {
    C = 1.0
    Tolerance = 1e-3
    MaxIterations = 100
    Verbose = false
}

// Train SVM with quantum kernel
match QuantumKernelSVM.train backend featureMap trainData trainLabels config 1000 with
| Ok model ->
    // Evaluate on test data
    let testData = [| [| 0.5; 0.5 |]; [| 0.2; 0.8 |] |]
    let testLabels = [| 0; 1 |]
    
    match QuantumKernelSVM.evaluate backend model testData testLabels 1000 with
    | Ok accuracy -> printfn "Test accuracy: %.2f%%" (accuracy * 100.0)
    | Error msg -> printfn "Evaluation error: %s" msg
| Error msg -> printfn "Training error: %s" msg
```

**QML Features:**
- VQC - Variational quantum circuits for supervised learning
- Quantum Kernels - Leverage quantum feature spaces in SVMs
- Feature Maps - ZZFeatureMap, PauliFeatureMap for encoding classical data
- Variational Forms - RealAmplitudes, EfficientSU2 ansatz circuits
- Adam Optimizer - Gradient-based training with momentum
- Model Serialization - Save/load trained models
- Data Preprocessing - Normalization, encoding, splits

**Examples:** 
- `examples/QML/VQCExample.fsx` - Complete VQC training pipeline
- `examples/QML/FeatureMapExample.fsx` - Feature encoding demonstrations
- `examples/QML/VariationalFormExample.fsx` - Ansatz circuit exploration

---

## Business Problem Builders

**High-level APIs for common business applications powered by quantum algorithms (Grover's search) and quantum machine learning.**

### Social Network Analyzer - Community Detection & Fraud Rings

**Use Grover's algorithm to find tight-knit communities (cliques) in social networks for marketing, fraud detection, and team analysis.**

```fsharp
open FSharp.Azure.Quantum.Business
open FSharp.Azure.Quantum.Backends.LocalBackend

// Define social network
let network = SocialNetworkAnalyzer.socialNetwork {
    // Add people
    people ["Alice"; "Bob"; "Carol"; "Dave"; "Eve"; "Frank"]
    
    // Define connections (friendships, transactions, communications)
    connections [
        ("Alice", "Bob")
        ("Bob", "Carol")
        ("Carol", "Alice")     // Triangle: potential community
        
        ("Dave", "Eve")
        ("Eve", "Frank")
        ("Frank", "Dave")      // Another triangle
        
        ("Carol", "Dave")      // Bridge between communities
    ]
    
    // Find communities of at least 3 people
    findCommunities 3
    
    // Use quantum acceleration (optional)
    backend (LocalBackend() :> IQuantumBackend)
    shots 1000
}

match network with
| Ok result ->
    printfn "Communities found: %d" result.Communities.Length
    
    for comm in result.Communities do
        printfn "Community: %A" comm.Members
        printfn "  Strength: %.0f%% connected" (comm.Strength * 100.0)
        printfn "  Internal connections: %d" comm.InternalConnections
| Error err -> printfn "Error: %A" err
```

**Business Use Cases:**
- **Marketing**: Identify influencer groups for targeted campaigns
- **Fraud Detection**: Detect fraud rings through circular transaction patterns
- **HR Analytics**: Analyze team collaboration and communication networks
- **Healthcare**: Track disease outbreak clusters and contact tracing
- **Security**: Find coordinated bot networks or insider threat groups

**Quantum Advantage:**
- Classical clique finding: O(2^n) exponential time complexity
- Grover's algorithm: O(√(2^n)) quadratic speedup
- Most beneficial for networks with 20+ people
- Real-time fraud detection at scale

**Features:**
- F# computation expression: `socialNetwork { }`
- Quantum backend support (LocalBackend, IonQ, Rigetti)
- Configurable shots for measurement accuracy
- Classical fallback for small networks
- Community strength metrics (connectivity percentage)

**Example:** `examples/GraphAnalytics/SocialNetworkAnalyzer_Example.fsx`

---

### Constraint Scheduler - Workforce & Resource Allocation

**Use quantum optimization (Max-SAT and Weighted Graph Coloring) to solve scheduling problems with hard and soft constraints.**

```fsharp
open FSharp.Azure.Quantum.Business
open FSharp.Azure.Quantum.Backends.LocalBackend

// Workforce scheduling with constraints
let schedule = ConstraintScheduler.constraintScheduler {
    // Define shifts to cover
    task "Morning"
    task "Afternoon"
    task "Evening"
    task "Night"
    
    // Available workers with hourly rates
    resource "Alice" 25.0   // Senior: $25/hour
    resource "Bob" 15.0     // Junior: $15/hour
    resource "Carol" 20.0   // Mid-level: $20/hour
    resource "Dave" 15.0    // Junior: $15/hour
    
    // Hard constraints (MUST be satisfied)
    conflict "Morning" "Afternoon"     // Can't work consecutive shifts
    conflict "Afternoon" "Evening"
    conflict "Evening" "Night"
    
    // Soft constraints (preferences with weights)
    prefer "Morning" "Alice" 10.0      // Alice prefers morning
    prefer "Afternoon" "Carol" 8.0     // Carol prefers afternoon
    prefer "Night" "Dave" 9.0          // Dave prefers night
    
    // Optimization goal
    optimizeFor ConstraintScheduler.MinimizeCost
    maxBudget 100.0
    
    // Use quantum optimization (optional)
    backend (LocalBackend() :> IQuantumBackend)
    shots 1500
}

match schedule with
| Ok result ->
    match result.BestSchedule with
    | Some sched ->
        printfn "Optimal Shift Assignments:"
        for assignment in sched.Assignments do
            printfn "  %s → %s ($%.2f/hour)" 
                assignment.Task 
                assignment.Resource 
                assignment.Cost
        
        printfn "\nTotal Cost: $%.2f" sched.TotalCost
        printfn "Constraints Satisfied: %d / %d hard, %d / %d soft" 
            sched.HardConstraintsSatisfied 
            sched.TotalHardConstraints
            sched.SoftConstraintsSatisfied 
            sched.TotalSoftConstraints
        printfn "Feasible: %b" sched.IsFeasible
    | None ->
        printfn "No feasible schedule found"
| Error err -> printfn "Error: %A" err
```

**Business Use Cases:**
- **Workforce Management**: Employee shift scheduling with availability constraints
- **Cloud Computing**: VM allocation to minimize costs while meeting SLAs
- **Manufacturing**: Production task assignment with equipment constraints
- **Logistics**: Delivery route optimization with time windows
- **Project Management**: Task assignment with dependencies and deadlines

**Quantum Advantage:**
- Classical constraint solving: NP-hard (exponential time)
- Quantum optimization: Quadratic speedup with Grover search
- Most beneficial for 10+ tasks with complex constraints

**Optimization Goals:**
- `MinimizeCost`: Uses Weighted Graph Coloring oracle
- `MaximizeSatisfaction`: Uses Max-SAT oracle for constraint satisfaction
- `Balanced`: Combines both cost and satisfaction criteria

**Constraint Types:**
- **Hard Constraints** (must satisfy): `conflict`, `require`, `precedence`
- **Soft Constraints** (preferences): `prefer` with configurable weights
- **Budget Constraints**: `maxBudget` for cost optimization

**Features:**
- F# computation expression: `constraintScheduler { }`
- Dual oracle support (Max-SAT and Weighted Graph Coloring)
- Quantum backend support (LocalBackend, IonQ, Rigetti)
- Configurable shots for accuracy vs. speed tradeoff
- Classical fallback for small problems
- Detailed constraint satisfaction metrics

**Example:** `examples/JobScheduling/ConstraintScheduler_Example.fsx`

---

### AutoML - Automated Machine Learning

```fsharp
open FSharp.Azure.Quantum.Business

// Training data: each sample is a float array of features, with a numeric label
let features = [| [| 0.1; 0.2 |]; [| 0.9; 0.8 |]; [| 0.2; 0.1 |]; [| 0.8; 0.9 |] |]
let labels   = [| 0.0; 1.0; 0.0; 1.0 |]

// The CE runs the automated model search and returns Result<AutoMLResult, _>
let automlResult =
    AutoML.autoML {
        trainWith features labels
        tryBinaryClassification true
        maxTrials 20
        validationSplit 0.2
    }

match automlResult with
| Ok result ->
    printfn "Best model: %s (validation score %.2f%%)" result.BestModelType (result.Score * 100.0)
    printfn "Best architecture: %A" result.BestArchitecture

    // Use the best model for a prediction
    match AutoML.predict [| 0.85; 0.85 |] result with
    | Ok prediction -> printfn "Prediction: %A" prediction
    | Error err -> printfn "Error: %A" err
| Error err -> printfn "Error: %A" err
```

### Anomaly Detection - Security & Fraud Detection

```fsharp
open FSharp.Azure.Quantum.Business

// Train on NORMAL examples only (one-class anomaly detection).
// Each sample is a float array of features (packet size, duration, failed logins).
let normalTraffic =
    [| [| 0.10; 0.20; 0.0 |]
       [| 0.15; 0.25; 0.0 |]
       [| 0.12; 0.18; 0.1 |] |]

// The CE trains the detector and returns Result<Detector, _>
let detectorResult =
    AnomalyDetector.anomalyDetection {
        trainOnNormalData normalTraffic
        sensitivity AnomalyDetector.High
        contaminationRate 0.05
    }

match detectorResult with
| Ok detector ->
    // Score a new sample against the trained detector
    let sample = [| 0.9; 0.95; 0.8 |]
    match AnomalyDetector.check sample detector with
    | Ok result ->
        printfn "Anomaly: %b (score %.3f)" result.IsAnomaly result.AnomalyScore
    | Error err -> printfn "Error: %A" err
| Error err -> printfn "Error: %A" err
```

### Binary Classification - Fraud Detection

```fsharp
open FSharp.Azure.Quantum.Business

// Labeled transactions: features as float arrays, labels 0 = legitimate, 1 = fraud
let trainX = [| [| 0.1; 0.2 |]; [| 0.9; 0.8 |]; [| 0.2; 0.1 |]; [| 0.85; 0.9 |] |]
let trainY = [| 0; 1; 0; 1 |]

// The CE trains the classifier and returns Result<Classifier, _>
let classifierResult =
    BinaryClassifier.binaryClassification {
        trainWith trainX trainY
        architecture BinaryClassifier.Hybrid   // Quantum feature map + classical SVM
        maxEpochs 50
    }

match classifierResult with
| Ok model ->
    // Evaluate on held-out data (features, labels, classifier)
    let testX = [| [| 0.15; 0.2 |]; [| 0.8; 0.85 |] |]
    let testY = [| 0; 1 |]
    match BinaryClassifier.evaluate testX testY model with
    | Ok metrics ->
        printfn "Precision: %.2f%%" (metrics.Precision * 100.0)
        printfn "Recall: %.2f%%" (metrics.Recall * 100.0)
        printfn "F1 Score: %.2f" metrics.F1Score
    | Error err -> printfn "Error: %A" err

    // Classify a new sample
    match BinaryClassifier.predict [| 0.82; 0.88 |] model with
    | Ok prediction ->
        printfn "Label: %d (%.2f%% confidence)" prediction.Label (prediction.Confidence * 100.0)
    | Error err -> printfn "Error: %A" err
| Error err -> printfn "Training failed: %A" err
```

### Predictive Modeling - Customer Churn Prediction

```fsharp
open FSharp.Azure.Quantum.Business

// Historical customers: features (e.g. tenure, monthly charges), target = churn score
let history = [| [| 12.0; 50.0 |]; [| 1.0; 90.0 |]; [| 24.0; 40.0 |]; [| 2.0; 95.0 |] |]
let targets = [| 0.0; 1.0; 0.0; 1.0 |]

// The CE trains the model and returns Result<Model, _>
let modelResult =
    PredictiveModel.predictiveModel {
        trainWith history targets
        problemType PredictiveModel.Regression
        architecture PredictiveModel.Hybrid
        maxEpochs 50
    }

match modelResult with
| Ok model ->
    // Score an active customer (backend / shots optional -> None None uses defaults)
    match PredictiveModel.predict [| 3.0; 92.0 |] model None None with
    | Ok prediction -> printfn "Predicted churn score: %.3f" prediction.Value
    | Error err -> printfn "Error: %A" err
| Error err -> printfn "Error: %A" err
```

### Similarity Search - Product Recommendations

Build a `SearchIndex` from a catalog with `similaritySearch { ... }`, then rank
neighbours with `SimilaritySearch.findSimilar queryItem queryFeatures topN index`
(returns `QuantumResult<SearchResults<'T>>`).

▶ Full runnable example: [`examples/SimilaritySearch/ProductRecommendations.fsx`](examples/SimilaritySearch/ProductRecommendations.fsx)

**Business Builder Features:**
- Social Network Analyzer - Community detection, fraud rings, influencer identification (Grover's algorithm)
- Constraint Scheduler - Workforce scheduling, resource allocation with constraints (Max-SAT & Graph Coloring)
- AutoML - Automated hyperparameter tuning, model selection, ensemble methods
- Anomaly Detection - Outlier detection for security, fraud, quality control
- Binary Classification - Two-class problems (fraud, spam, churn)
- Predictive Modeling - Time-series forecasting, demand prediction
- Similarity Search - Recommendations, semantic search, clustering
- Quantum-Enhanced - Leverages Grover's search, quantum kernels, and feature maps
- Ready for Production - Model serialization, evaluation metrics, validation

**Examples:**
- `examples/GraphAnalytics/SocialNetworkAnalyzer_Example.fsx` - Community detection and fraud ring identification
- `examples/JobScheduling/ConstraintScheduler_Example.fsx` - Workforce and resource scheduling
- `examples/AutoML/QuickPrototyping.fsx` - Complete AutoML pipeline
- `examples/AnomalyDetection/SecurityThreatDetection.fsx` - Network security monitoring
- `examples/BinaryClassification/FraudDetection.fsx` - Transaction fraud detection
- `examples/PredictiveModeling/CustomerChurnPrediction.fsx` - Churn prediction
- `examples/SimilaritySearch/ProductRecommendations.fsx` - E-commerce recommendations

---

## HybridSolver - Automatic Classical/Quantum Routing

**Smart solver that automatically chooses between classical and quantum execution based on problem analysis.**

The HybridSolver provides a unified API that:
- Analyzes problem size, structure, and complexity
- Estimates quantum advantage potential
- Routes to classical solver (fast, free) OR quantum backend (scalable, expensive)
- Provides reasoning for solver selection
- Optionally compares both methods for validation

**Decision Framework:**
- Small problems (< 50 variables) → Classical solver (milliseconds, $0)
- Large problems (> 100 variables) → Quantum solver (seconds-minutes, ~$10-100)
- Automatic cost guards and recommendations

### Supported Problems

The HybridSolver supports all 5 main optimization problems:

```fsharp
open FSharp.Azure.Quantum.HybridSolver

// TSP with automatic routing
let distances = array2D [[0.0; 10.0; 15.0]; 
                          [10.0; 0.0; 20.0]; 
                          [15.0; 20.0; 0.0]]

match solveTsp distances None None None with
| Ok solution ->
    printfn "Method used: %A" solution.Method           // Classical or Quantum
    printfn "Reasoning: %s" solution.Reasoning          // Why this method?
    printfn "Time: %.2f ms" solution.ElapsedMs
    printfn "Route: %A" solution.Result.Route
    printfn "Distance: %.2f" solution.Result.TotalDistance
| Error msg -> printfn "Error: %s" msg

// MaxCut with quantum backend config
let vertices = ["A"; "B"; "C"; "D"]
let edges = [("A", "B", 1.0); ("B", "C", 2.0); ("C", "D", 1.0)]
let problem = MaxCut.createProblem vertices edges

let quantumConfig = {
    Backend = IonQ "ionq.simulator"
    WorkspaceId = "your-workspace-id"
    Location = "eastus"
    ResourceGroup = "quantum-rg"
    SubscriptionId = "sub-id"
    MaxCostUSD = Some 50.0          // Cost guard
    EnableComparison = true         // Compare with classical
}

match solveMaxCut problem (Some quantumConfig) None None with
| Ok solution ->
    printfn "Method: %A" solution.Method
    printfn "Cut Value: %.2f" solution.Result.CutValue
    match solution.Recommendation with
    | Some recommendation -> printfn "Advisor: %s" recommendation.Reasoning
    | None -> ()
| Error msg -> printfn "Error: %s" msg

// Knapsack
match solveKnapsack knapsackProblem None None None with
| Ok solution ->
    printfn "Total Value: %.2f" solution.Result.TotalValue
    printfn "Items: %A" solution.Result.SelectedItems
| Error msg -> printfn "Error: %s" msg

// Graph Coloring
match solveGraphColoring graphProblem 3 None None None with
| Ok solution ->
    printfn "Colors Used: %d/3" solution.Result.ColorsUsed
    printfn "Valid: %b" solution.Result.IsValid
| Error msg -> printfn "Error: %s" msg

// Portfolio Optimization
match solvePortfolio portfolioProblem None None None with
| Ok solution ->
    printfn "Portfolio Value: $%.2f" solution.Result.TotalValue
    printfn "Expected Return: %.2f%%" (solution.Result.ExpectedReturn * 100.0)
| Error msg -> printfn "Error: %s" msg
```

### Features

- Unified API: Single function call for any problem size
- Smart Routing: Automatic classical/quantum decision
- Cost Guards: `MaxCostUSD` prevents runaway quantum costs
- Validation Mode: `EnableComparison = true` runs both methods
- Transparent Reasoning: Explains why each method was chosen
- Quantum Advisor: Provides recommendations on quantum readiness

### When to Use HybridSolver vs Direct Builders

**Use HybridSolver when:**
- Problem size varies (sometimes small, sometimes large)
- You want automatic cost optimization
- You need validation/comparison between classical and quantum
- You're prototyping and unsure which approach is better

**Use Direct Builders when:**
- You always want quantum (for research/learning)
- Problem size is consistently in quantum range (10-20 qubits)
- You need fine-grained control over backend configuration
- You're integrating with specific QAOA parameter tuning

**Location:** `src/FSharp.Azure.Quantum/Solvers/Hybrid/HybridSolver.fs`  
**Status:** Recommended for production deployments

---

## Architecture

### Intent-First Algorithms (Backend-Aware Planning)

Some algorithms (notably Grover-family building blocks such as **Amplitude Amplification**) are implemented as **intent → plan → execute** rather than as a single canonical gate circuit.

- On gate-native backends, this may lower to standard gate operations.
- On non-gate-native backends (e.g., topological models), the same intent can be executed with native semantic operations.
- If a backend can’t support an algorithm’s intent and there is no valid lowering, the library should fail explicitly rather than silently producing an incorrect result.

This is primarily a portability/correctness feature; most users won’t need to change code. Details: `docs/adr-intent-first-algorithms.md`.



### 3-Layer Quantum-Only Architecture

```mermaid
graph TB
    subgraph "Layer 1: High-Level Builders"
        GC["GraphColoring Builder<br/>graphColoring { }"]
        MC["MaxCut Builder<br/>MaxCut.createProblem"]
        KS["Knapsack Builder<br/>Knapsack.createProblem"]
        TS["TSP Builder<br/>TSP.createProblem"]
        PO["Portfolio Builder<br/>Portfolio.createProblem"]
        NF["NetworkFlow Builder<br/>NetworkFlow module"]
        SCHED["TaskScheduling Builder<br/>scheduledTask { }"]
    end
    
    subgraph "Layer 2: Quantum Solvers"
        QGC["QuantumGraphColoringSolver<br/>(QAOA)"]
        QMC["QuantumMaxCutSolver<br/>(QAOA)"]
        QKS["QuantumKnapsackSolver<br/>(QAOA)"]
        QTS["QuantumTspSolver<br/>(QAOA)"]
        QPO["QuantumPortfolioSolver<br/>(QAOA)"]
        QNF["QuantumNetworkFlowSolver<br/>(QAOA)"]
        QSCHED["QuantumSchedulingSolver<br/>(QAOA)"]
    end
    
    subgraph "Layer 3: Quantum Backends"
        LOCAL["LocalBackend<br/>(≤20 qubits)"]
        IONQ["IonQBackend<br/>(Azure Quantum)"]
        RIGETTI["RigettiBackend<br/>(Azure Quantum)"]
        ATOM["AtomComputingBackend<br/>(Azure Quantum, 100+ qubits)"]
        QUANTINUUM["QuantinuumBackend<br/>(Azure Quantum, 99.9%+ fidelity)"]
    end
    
    GC --> QGC
    MC --> QMC
    KS --> QKS
    TS --> QTS
    PO --> QPO
    NF --> QNF
    SCHED --> QSCHED
    
    QGC --> LOCAL
    QMC --> LOCAL
    QKS --> LOCAL
    QTS --> LOCAL
    QPO --> LOCAL
    QNF --> LOCAL
    QSCHED --> LOCAL
    
    QGC -.-> IONQ
    QMC -.-> IONQ
    QKS -.-> IONQ
    QTS -.-> IONQ
    QPO -.-> IONQ
    QNF -.-> IONQ
    QSCHED -.-> IONQ
    
    QGC -.-> RIGETTI
    QMC -.-> RIGETTI
    QKS -.-> RIGETTI
    QTS -.-> RIGETTI
    QPO -.-> RIGETTI
    QNF -.-> RIGETTI
    QSCHED -.-> RIGETTI
    
    QGC -.-> ATOM
    QMC -.-> ATOM
    QKS -.-> ATOM
    QTS -.-> ATOM
    QPO -.-> ATOM
    QNF -.-> ATOM
    QSCHED -.-> ATOM
    
    QGC -.-> QUANTINUUM
    QMC -.-> QUANTINUUM
    QKS -.-> QUANTINUUM
    QTS -.-> QUANTINUUM
    QPO -.-> QUANTINUUM
    QNF -.-> QUANTINUUM
    QSCHED -.-> QUANTINUUM
    
    style GC fill:#90EE90
    style MC fill:#90EE90
    style KS fill:#90EE90
    style TS fill:#90EE90
    style PO fill:#90EE90
    style NF fill:#90EE90
    style SCHED fill:#90EE90
    style QGC fill:#FFA500
    style QMC fill:#FFA500
    style QKS fill:#FFA500
    style QTS fill:#FFA500
    style QPO fill:#FFA500
    style QNF fill:#FFA500
    style QSCHED fill:#FFA500
    style LOCAL fill:#5B9BD5
    style IONQ fill:#70AD47
    style RIGETTI fill:#ED7D31
    style ATOM fill:#FFC000
    style QUANTINUUM fill:#A85CC8
```

### Layer Responsibilities

#### **Layer 1: High-Level Builders** 🟢
**Who uses it:** End users (F# and C# developers)  
**Purpose:** Business domain APIs with problem-specific validation

**Features:**
- F# computation expressions (`graphColoring { }`)
- C# fluent APIs (`CSharpBuilders.MaxCutProblem()`)
- Type-safe problem specification
- Domain-specific validation
- Automatic backend creation (defaults to LocalBackend)

**Example:**
```fsharp
// F# computation expression
let problem = graphColoring {
    node "R1" ["R2"]
    colors ["Red"; "Blue"]
}

// Delegates to Layer 2
GraphColoring.solve problem 2 None
```

#### **Layer 2: Quantum Solvers** 🟠
**Who uses it:** High-level builders (internal delegation)  
**Purpose:** QAOA implementations for specific problem types

**Features:**
- Problem → QUBO encoding
- QAOA circuit construction
- Variational parameter optimization (Nelder-Mead)
- Solution decoding and validation
- Backend-agnostic (accepts `IQuantumBackend`)

**Example:**
```fsharp
// Called internally by GraphColoring.solve
QuantumGraphColoringSolver.solve 
    backend          // IQuantumBackend
    problem          // GraphColoringProblem
    quantumConfig    // QAOA parameters
```

#### **Layer 3: Quantum Backends** 🔵
**Who uses it:** Quantum solvers  
**Purpose:** Quantum circuit execution

**Backend Types:**

| Backend | Qubits | Speed | Cost | Use Case |
|---------|--------|-------|------|----------|
| **LocalBackend** | ≤20 | Fast (ms) | Free | Development, testing, small problems |
| **IonQBackend** | 29+ (sim), 11 (QPU) | Moderate (seconds) | Paid | Production, large problems |
| **RigettiBackend** | 40+ (sim), 80 (QPU) | Moderate (seconds) | Paid | Production, large problems |
| **AtomComputingBackend** | 100+ (sim/QPU Phoenix) | Moderate (seconds) | Paid | Large-scale problems, all-to-all connectivity |
| **QuantinuumBackend** | 20-32 (sim), 20-32 (QPU) | Moderate (seconds) | Paid | High-fidelity (99.9%+), trapped-ion |

**Example:**
```fsharp
// Local simulation (default)
let backend = LocalBackend() :> IQuantumBackend

// Azure Quantum (cloud)
let connectionString = "InstrumentationKey=..."

// Using Azure Quantum Workspace (Recommended for cloud backends)
open FSharp.Azure.Quantum.Backends.AzureQuantumWorkspace

let workspace = createDefault "subscription-id" "resource-group" "workspace-name" "eastus"

// Using Azure Quantum Workspace (Recommended)
open FSharp.Azure.Quantum.Backends.AzureQuantumWorkspace

let workspace = createDefault "subscription-id" "resource-group" "workspace-name" "eastus"

// IonQ Backend
let backend_ionq = BackendAbstraction.createFromWorkspace workspace "ionq.simulator"

// Rigetti Backend  
let backend_rigetti = BackendAbstraction.createFromWorkspace workspace "rigetti.sim.qvm"

// Quantinuum Backend (trapped-ion, highest fidelity)
let backend_quantinuum = BackendAbstraction.createFromWorkspace workspace "quantinuum.sim.h1-1sc"

// Atom Computing Backend (neutral atoms, 100+ qubits, all-to-all connectivity)
let backend_atom = BackendAbstraction.createFromWorkspace workspace "atom-computing.sim"

// Rigetti Backend (superconducting, fast gates)
let backend_rigetti = BackendAbstraction.createRigettiBackend(
    connectionString = "YOUR_CONNECTION_STRING",
    targetId = "rigetti.sim.qvm"  // or "rigetti.qpu.*" for hardware
)

// Atom Computing Backend (neutral atoms, 100+ qubits, all-to-all connectivity)
let backend_atom = BackendAbstraction.createAtomComputingBackend(
    connectionString = "YOUR_CONNECTION_STRING",
    targetId = "atom-computing.sim"  // or "atom-computing.qpu.phoenix" for hardware
)

// Quantinuum Backend (trapped-ion, highest fidelity)
let backend_quantinuum = BackendAbstraction.createQuantinuumBackend(
    connectionString = "YOUR_CONNECTION_STRING",
    targetId = "quantinuum.sim.h1-1sc"  // See available targets below
)

// Pass to solver
match GraphColoring.solve problem 3 (Some backend_quantinuum) with
| Ok solution -> 
    printfn "Backend used: %s" solution.BackendName
```

**Quantinuum Targets:**
- `quantinuum.sim.h1-1sc` - H1-1 System Model SC simulator (20 qubits)
- `quantinuum.sim.h1-1e` - H1-1 System Model E simulator (20 qubits)
- `quantinuum.qpu.h1-1` - H1-1 hardware (20 qubits, 99.9%+ fidelity)
- `quantinuum.qpu.h2-1` - H2-1 hardware (32 qubits, 99.9%+ fidelity)

**Quantinuum Features:**
- ✅ **All-to-all connectivity** - No SWAP routing needed (trapped-ion architecture)
- ✅ **99.9%+ gate fidelity** - Highest quality quantum gates commercially available
- ✅ **Native gates**: H, X, Y, Z, S, T, RX, RY, RZ, CZ (no transpilation for phase gates!)
- ✅ **OpenQASM 2.0 format** - Standard quantum circuit language
- ✅ **Mid-circuit measurement** - Advanced quantum features
- ⚠️ **Premium pricing** - Higher cost per shot than IonQ/Rigetti


### D-Wave Quantum Annealer

```fsharp
open FSharp.Azure.Quantum.Backends.DWaveBackend
open FSharp.Azure.Quantum.Backends.RealDWaveBackend
open FSharp.Azure.Quantum.Backends.DWaveTypes

// Option 1: Mock D-Wave backend (testing, no credentials needed)
let mockBackend = createMockDWaveBackend Advantage_System6_1 (Some 42)

// Option 2: Real D-Wave backend (production, requires API token)
let dwaveConfig = {
    ApiToken = "YOUR_DWAVE_TOKEN"  // Get from https://cloud.dwavesys.com/leap/
    Endpoint = "https://cloud.dwavesys.com/sapi/v2/"
    Solver = "Advantage_system6.1"  // 5640 qubits, Pegasus topology
    TimeoutMs = Some 300000  // 5 minutes
}
let dwaveBackend = RealDWaveBackend.create dwaveConfig

// Option 3: From environment variables (DWAVE_API_TOKEN, DWAVE_SOLVER)
match RealDWaveBackend.createFromEnv() with
| Ok backend -> printfn "D-Wave backend ready: %s" backend.Name
| Error msg -> printfn "No D-Wave credentials: %s" msg

// Build QAOA circuit for MaxCut (automatically converted to QUBO/Ising)
let vertices = ["A"; "B"; "C"; "D"; "E"]
let edges = [
    ("A", "B", 1.0); ("B", "C", 2.0); ("C", "D", 1.0)
    ("D", "E", 1.5); ("E", "A", 1.2)
]

let problem = MaxCut.createProblem vertices edges

// Solve using D-Wave backend (implements IQuantumBackend)
// D-Wave automatically extracts QUBO from QAOA circuit and uses quantum annealing
match MaxCut.solve problem (Some dwaveBackend) with
| Ok solution ->
    printfn "Cut value: %.2f" solution.CutValue
    printfn "Partition S: %A" solution.PartitionS
    printfn "Partition T: %A" solution.PartitionT
| Error msg -> printfn "Error: %s" msg
```

**D-Wave Features:**
- 2000-5640 qubits - Far larger than gate-based quantum computers (Advantage series)
- Implements IQuantumBackend - Seamless integration with QAOA solvers
- Automatic QUBO extraction - Converts QAOA circuits to native Ising format
- Quantum annealing - Different paradigm than gate-based (finds ground states via annealing)
- Mock backend - Test without credentials using classical simulated annealing
- Real backend - Production D-Wave Leap Cloud API integration (pure .NET, no Python)
- Production hardware - Available now (Advantage_system6.1: 5640 qubits)
- Specialized - Best for optimization problems (not universal quantum computing)

**Available D-Wave Solvers:**
- `Advantage_System6_1`: 5640 qubits (Pegasus topology, latest)
- `Advantage_System4_1`: 5000 qubits (Pegasus topology)
- `Advantage2_Prototype`: 1200 qubits (Zephyr topology, next-gen)
- `DW_2000Q_6`: 2048 qubits (Chimera topology, legacy)

**Example:** `examples/DWaveMaxCutExample.fsx`

### Backend Comparison

```fsharp
// Small problem: Use local simulation
let smallProblem = MaxCut.createProblem ["A"; "B"; "C"] [("A","B",1.0)]
let result1 = MaxCut.solve smallProblem None  // Fast, free

// Medium problem: Use Azure Quantum
let mediumProblem = 
    MaxCut.createProblem 
        [for i in 1..20 -> sprintf "V%d" i]
        [for i in 1..19 -> (sprintf "V%d" i, sprintf "V%d" (i+1), 1.0)]

let azureBackend = BackendAbstraction.createIonQBackend(conn, "ionq.simulator")
let result2 = MaxCut.solve mediumProblem (Some azureBackend)  // 20-29 qubits

// Large problem: Use D-Wave quantum annealer
let largeProblem =
    MaxCut.createProblem
        [for i in 1..100 -> sprintf "V%d" i]  // 100 vertices!
        [for i in 1..99 -> (sprintf "V%d" i, sprintf "V%d" (i+1), 1.0)]

// Create D-Wave backend (mock for testing or real for production)
let dwaveBackend = 
    // Option 1: Mock backend (no credentials)
    DWaveBackend.createMockDWaveBackend Advantage_System6_1 None
    
    // Option 2: Real backend (requires DWAVE_API_TOKEN env var)
    // match RealDWaveBackend.createFromEnv() with
    // | Ok backend -> backend
    // | Error _ -> DWaveBackend.createDefaultMockBackend()

let result3 = MaxCut.solve largeProblem (Some dwaveBackend)  // 2000+ qubits
```

**Backend Selection Guide:**

| Problem Size | Backend | Qubits | Speed | Cost | Best For |
|--------------|---------|--------|-------|------|----------|
| **Small** (≤20 variables) | LocalBackend | ≤20 | Milliseconds | Free | Development, testing, prototyping |
| **Medium** (20-29 variables) | IonQ/Rigetti | 29-80 | Seconds | ~$10-50/run | Gate-based quantum algorithms (QAOA, VQE) |
| **Medium-High Fidelity** (20-32 variables) | Quantinuum | 20-32 | Seconds | ~$50-100/run | High-precision quantum chemistry, error-sensitive algorithms |
| **Large** (30-100+ variables) | Atom Computing | 100+ | Seconds | ~$20-80/run | Large-scale optimization, all-to-all connectivity benefits |
| **Very Large** (100+ variables) | D-Wave | 2000+ | Seconds | ~$1-10/run | Optimization problems (MaxCut, TSP, scheduling) |

**When to use D-Wave:**
- Optimization problems with 50+ variables
- QUBO/Ising problems (MaxCut, Knapsack, Graph Coloring)
- Production workloads needing large problem sizes
- Cost-sensitive applications (D-Wave cheaper per qubit)
- NOT for: QFT-based algorithms, Grover's search, quantum chemistry (use gate-based)

### Unified Backend Architecture

All quantum backends implement the **`IQuantumBackend`** interface, providing a consistent API for quantum circuit execution regardless of the underlying hardware or simulation technology.

**Core Interface** (`src/FSharp.Azure.Quantum/Core/BackendAbstraction.fs`):

```fsharp
type IQuantumBackend =
    /// Execute circuit and return quantum state (not just measurements)
    abstract member ExecuteToState: ICircuit -> Result<QuantumState, QuantumError>
    
    /// Apply single operation to existing state
    abstract member ApplyOperation: QuantumOperation -> QuantumState -> Result<QuantumState, QuantumError>
    
    /// Check if backend supports a specific operation
    abstract member SupportsOperation: QuantumOperation -> bool
    
    /// Initialize quantum state for n qubits
    abstract member InitializeState: int -> Result<QuantumState, QuantumError>
    
    /// Backend's native state representation
    abstract member NativeStateType: QuantumStateType
    
    /// Backend identifier
    abstract member Name: string
```

**State Types:**
- `StateVector` - Full complex amplitude representation (LocalBackend, most gate-based)
- `TopologicalBraiding` - Anyon braiding representation (TopologicalBackend)
- `Sparse` - Sparse matrix representation (for large systems)
- `Mixed` - Density matrix representation (noisy simulations)

**Key Benefits:**

1. **State-Based Execution**: Get quantum states for inspection and manipulation, not just shot-based measurements
   ```fsharp
   let! state = backend.ExecuteToState circuit
   let amplitudes = state.GetAmplitudes()  // Inspect quantum state directly
   ```

2. **Backend-Agnostic Code**: Write algorithms once, run on any backend
   ```fsharp
   // Works with LocalBackend, IonQBackend, DWaveBackend, etc.
   let runQFT (backend: IQuantumBackend) n =
       result {
           let circuit = CircuitBuilder.create n |> QFT.apply
           let! state = backend.ExecuteToState circuit
           return state
       }
   ```

3. **Multi-Stage Algorithms**: Chain operations across multiple backends
   ```fsharp
   // QFT → QPE → Measurement on different backends
   let! qftState = localBackend.ExecuteToState qftCircuit
   let! qpeState = cloudBackend.ApplyOperation qpeOperation qftState
   let! final = cloudBackend.ApplyOperation measurement qpeState
   ```

4. **Capability Checking**: Verify backend support before execution
   ```fsharp
   if backend.SupportsOperation (Gate (Toffoli (0, 1, 2))) then
       // Use native Toffoli
   else
       // Fall back to decomposed version
   ```

**Example - Backend Switching:**

```fsharp
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Backends.LocalBackend

// Step 1: Develop locally
let localBackend = LocalBackend()
let circuit = CircuitBuilder.create 3
              |> addGate (H 0)
              |> addGate (CNOT (0,1))
              |> addGate (CNOT (1,2))

match localBackend.ExecuteToState circuit with
| Ok state -> 
    printfn "Local test passed: %d amplitudes" (state.GetAmplitudes().Length)
| Error e -> 
    printfn "Error: %A" e

// Step 2: Same code on cloud backend (no changes needed!)
let workspace = AzureQuantumWorkspace.createDefault "sub-id" "rg" "workspace" "eastus"
let cloudBackend = createFromWorkspace workspace "ionq.simulator"

match cloudBackend.ExecuteToState circuit with  // Identical call!
| Ok state -> 
    printfn "Cloud execution passed: Backend=%s" cloudBackend.Name
| Error e -> 
    printfn "Error: %A" e
```

**All Backends Implement IQuantumBackend:**
- ✅ **LocalBackend** - Local state-vector simulation
- ✅ **IonQBackend** - IonQ gate-based quantum computers
- ✅ **RigettiBackend** - Rigetti superconducting QPUs
- ✅ **QuantinuumBackend** - Quantinuum trapped-ion systems
- ✅ **AtomComputingBackend** - Atom Computing neutral atom QPUs
- ✅ **DWaveBackend** - D-Wave quantum annealer (converts QAOA to QUBO)
- ✅ **TopologicalBackend** - Topological quantum simulation

This unified interface enables seamless integration of the library's high-level solvers (QAOA, QFT, Grover) with any supported quantum hardware or simulator.

### Azure Quantum Workspace Management

**Production-ready hybrid approach: Workspace quota management + proven HTTP backends**

```fsharp
open FSharp.Azure.Quantum.Backends.AzureQuantumWorkspace
open FSharp.Azure.Quantum.Core.BackendAbstraction
open System.Net.Http

// Step 1: Check quota with workspace
use workspace = 
    createDefault 
        "your-subscription-id"
        "your-resource-group"
        "your-workspace-name"
        "eastus"

async {
    // Check remaining quota before execution
    let! quota = workspace.GetTotalQuotaAsync()
    
    match quota.Remaining with
    | Some remaining when remaining < 10.0 ->
        printfn "⚠️  Low quota - stopping"
    | Some remaining ->
        printfn "✅ Sufficient quota: %.2f credits" remaining
        
        // Step 2: Use HTTP backend for proven execution
        use httpClient = new HttpClient()
        let backend = createIonQBackend
            httpClient
            "https://your-workspace.quantum.azure.com"
            "ionq.simulator"
        
        // Step 3: Convert circuit and execute
        let circuit = quantumCircuit { H 0; CNOT 0 1 }
        let wrapper = CircuitWrapper(circuit) :> ICircuit
        
        match convertCircuitToProviderFormat wrapper "ionq.simulator" with
        | Ok json ->
            match backend.Execute wrapper 1000 with
            | Ok result -> printfn "Success!"
            | Error msg -> printfn "Error: %s" msg
        | Error msg -> 
            printfn "Circuit conversion failed: %s" msg
    | None -> 
        printfn "✅ Unlimited quota"
} |> Async.RunSynchronously
```

**What you get:**
- Workspace Features: Quota checking, provider discovery, credential management
- Circuit Conversion: Automatic provider-specific format conversion (IonQ JSON, Rigetti Quil)
- Proven Backends: Full HTTP-based job submission, polling, and result parsing
- Resource Safety: IDisposable pattern for proper cleanup

**Environment-Based Configuration:**
```fsharp
// Set environment variables:
// export AZURE_QUANTUM_SUBSCRIPTION_ID="..."
// export AZURE_QUANTUM_RESOURCE_GROUP="..."
// export AZURE_QUANTUM_WORKSPACE_NAME="..."
// export AZURE_QUANTUM_LOCATION="eastus"

match createFromEnvironment() with
| Ok workspace -> 
    printfn "✅ Workspace loaded: %s" workspace.Config.WorkspaceName
| Error msg -> 
    printfn "⚠️  Environment not configured: %s" msg
```

**Circuit Format Conversion:**
```fsharp
// Convert circuits to provider-specific formats
let circuit = quantumCircuit { H 0; CNOT 0 1; RX (0, Math.PI / 4.0) }
let wrapper = CircuitWrapper(circuit) :> ICircuit

// To IonQ JSON
match convertCircuitToProviderFormat wrapper "ionq.simulator" with
| Ok ionqJson -> printfn "IonQ: %s" ionqJson
| Error msg -> printfn "Error: %s" msg

// To Rigetti Quil
match convertCircuitToProviderFormat wrapper "rigetti.sim.qvm" with
| Ok quilProgram -> printfn "Quil: %s" quilProgram
| Error msg -> printfn "Error: %s" msg
```

**Benefits:**
- Workspace management without SDK complexity
- Automatic gate transpilation for backend compatibility
- Support for CircuitWrapper and QaoaCircuitWrapper
- IonQ, Rigetti, Atom Computing, and Quantinuum providers

**Example:** See `examples/AzureQuantumWorkspace/WorkspaceExample.fsx`

### SDK Backend - Full Azure Quantum Integration

**Complete SDK-powered backend using Microsoft.Azure.Quantum.Client**

```fsharp
open FSharp.Azure.Quantum.Backends.AzureQuantumWorkspace
open FSharp.Azure.Quantum.Core.BackendAbstraction

// Step 1: Create workspace
use workspace = 
    createDefault 
        "your-subscription-id"
        "your-resource-group"
        "your-workspace-name"
        "eastus"

// Step 2: Create SDK backend (NEW!)
let backend = createFromWorkspace workspace "ionq.simulator"

// Step 3: Build circuit
let circuit = quantumCircuit {
    H 0
    CNOT 0 1
    MEASURE_ALL
}

let wrapper = CircuitWrapper(circuit) :> ICircuit

// Step 4: Execute on Azure Quantum
match backend.Execute wrapper 1000 with
| Ok result ->
    printfn "✅ Job completed!"
    printfn "   Backend: %s" result.BackendName
    printfn "   Shots: %d" result.NumShots
    printfn "   Job ID: %s" (result.Metadata.["job_id"] :?> string)
    
    // Analyze measurements
    let counts = result.Measurements |> Array.countBy id
    counts |> Array.iter (fun (bitstring, count) ->
        printfn "   %A: %d times" bitstring count)
| Error msg ->
    printfn "❌ Error: %s" msg
```

**SDK Backend Features:**
- Full Job Lifecycle: Submit → Poll → Retrieve results (all automated)
- Automatic Circuit Conversion: IonQ JSON / Rigetti Quil format
- Smart Polling: Exponential backoff (1s → 30s max delay)
- Rich Metadata: job_id, provider, target, status in results
- Histogram Parsing: Automatic extraction of measurement distributions
- Resource Safety: IDisposable workspace for cleanup
- Workspace Integration: Uses Microsoft.Azure.Quantum SDK internally

**SDK Backend with Quota Check:**
```fsharp
async {
    // Check quota before execution
    let! quota = workspace.GetTotalQuotaAsync()
    
    match quota.Remaining with
    | Some remaining when remaining < 10.0 ->
        printfn "⚠️  Low quota: %.2f credits - stopping" remaining
    | Some remaining ->
        printfn "✅ Quota available: %.2f credits" remaining
        
        // Create backend and execute
        let backend = createFromWorkspace workspace "ionq.simulator"
        match backend.Execute circuit 1000 with
        | Ok result -> printfn "Success!"
        | Error msg -> printfn "Error: %s" msg
    | None ->
        printfn "✅ Unlimited quota"
        // Execute...
} |> Async.RunSynchronously
```

**Backend Comparison:**

| Feature | LocalBackend | HTTP Backend | SDK Backend |
|---------|-------------|--------------|-------------------|
| **Setup** | None | HttpClient + URL | Workspace object |
| **Quota Checking** | ❌ | ❌ | ✅ |
| **Provider Discovery** | ❌ | ❌ | ✅ |
| **Job Polling** | ❌ (instant) | Manual | ✅ Automatic |
| **Resource Cleanup** | ❌ | Manual | ✅ IDisposable |
| **Circuit Conversion** | ❌ | Manual | ✅ Automatic |
| **Max Qubits** | 20 | 29 (IonQ) / 40 (Rigetti) | 29 (IonQ) / 40 (Rigetti) |
| **Cost** | Free | Paid | Paid |
| **Production Ready** | ✅ | ✅ | ✅ |
| **Best For** | Testing | Manual control | Full integration |

**When to use each backend:**
- **LocalBackend:** Development, testing, small circuits (<20 qubits), free tier
- **HTTP Backend:** Production workloads, proven stability, fine-grained control
- **SDK Backend:** Full workspace features, quota management, easier setup, complete integration

**Example:** See `examples/AzureQuantumWorkspace/WorkspaceExample.fsx` (Examples 7-9)

---

## OpenQASM 2.0 Support

**Import and export quantum circuits to IBM Qiskit, Cirq, and other OpenQASM-compatible platforms.**

### Why OpenQASM?

OpenQASM (Open Quantum Assembly Language) is the **industry-standard text format** for quantum circuits:
- IBM Qiskit - Primary format (6.7k GitHub stars)
- Amazon Braket - Native support
- Google Cirq - Import/export compatibility
- Interoperability - Share circuits between platforms

### Export Circuits to OpenQASM

**F# API:**
```fsharp
// open FSharp.Azure.Quantum
// open FSharp.Azure.Quantum.CircuitBuilder

// Build circuit using F# circuit builder
let circuit = 
    CircuitBuilder.empty 2
    |> CircuitBuilder.addGate (H 0)
    |> CircuitBuilder.addGate (CNOT (0, 1))
    |> CircuitBuilder.addGate (RZ (0, System.Math.PI / 4.0))

// Export to OpenQASM 2.0 string
let qasmCode = OpenQasm.export circuit
printfn "%s" qasmCode

// Export to .qasm file
OpenQasm.exportToFile circuit "bell_state.qasm"
```

**Output (`bell_state.qasm`):**
```qasm
OPENQASM 2.0;
include "qelib1.inc";
qreg q[2];
h q[0];
cx q[0],q[1];
rz(0.7853981634) q[0];
```

### Import Circuits from OpenQASM

**F# API:**
```fsharp
// open FSharp.Azure.Quantum
// open System.IO

// Parse OpenQASM string
let qasmCode = """
OPENQASM 2.0;
include "qelib1.inc";
qreg q[3];
h q[0];
cx q[0],q[1];
cx q[1],q[2];
"""

match OpenQasmImport.parse qasmCode with
| Ok circuit ->
    printfn "Loaded %d-qubit circuit with %d gates" 
        circuit.QubitCount circuit.Gates.Length
    // Use circuit with LocalBackend or export to another format
| Error msg -> 
    printfn "Parse error: %s" msg

// Import from file
match OpenQasmImport.parseFromFile "grover.qasm" with
| Ok circuit -> printfn "Loaded %d-qubit circuit" circuit.QubitCount
| Error msg -> printfn "Error: %s" msg
```

### C# API

```csharp
using FSharp.Azure.Quantum;
using FSharp.Azure.Quantum.CircuitBuilder;

// Export circuit to OpenQASM
var circuit = CircuitBuilder.empty(2)
    .AddGate(Gate.NewH(0))
    .AddGate(Gate.NewCNOT(0, 1));

var qasmCode = OpenQasm.export(circuit);
File.WriteAllText("circuit.qasm", qasmCode);

// Import from OpenQASM
var qasmInput = File.ReadAllText("qiskit_circuit.qasm");
var result = OpenQasmImport.parse(qasmInput);

if (result.IsOk) {
    var imported = result.ResultValue;
    Console.WriteLine($"Loaded {imported.QubitCount}-qubit circuit");
}
```

### Supported Gates

**All standard OpenQASM 2.0 gates supported:**

| Category | Gates |
|----------|-------|
| **Pauli** | X, Y, Z, H |
| **Phase** | S, S†, T, T† |
| **Rotation** | RX(θ), RY(θ), RZ(θ) |
| **Two-qubit** | CNOT (CX), CZ, SWAP |
| **Three-qubit** | CCX (Toffoli) |

### Workflow: Qiskit → F# → IonQ

**Full interoperability workflow:**

```fsharp
// 1. Load circuit from Qiskit
let qiskitCircuit = OpenQasmImport.parseFromFile "qiskit_algorithm.qasm"

match qiskitCircuit with
| Ok circuit ->
    // 2. Run on LocalBackend for testing
    let localBackend = LocalBackend() :> IQuantumBackend
    let testResult = LocalSimulator.QaoaSimulator.simulate circuit 1000
    
    printfn "Local test: %d samples" testResult.Shots
    
    // 3. Transpile for IonQ hardware
    let transpiled = GateTranspiler.transpileForBackend "ionq.qpu" circuit
    
    // 4. Execute on IonQ
    let ionqBackend = BackendAbstraction.createIonQBackend(
        connectionString,
        "ionq.qpu"
    )
    
    // 5. Export results back to Qiskit format
    OpenQasm.exportToFile transpiled "results_ionq.qasm"
| Error msg -> 
    printfn "Import failed: %s" msg
```

### Round-Trip Compatibility

**Circuits are preserved through export/import:**

```fsharp
// Original circuit
let original = { QubitCount = 3; Gates = [H 0; CNOT (0, 1); RZ (1, 1.5708)] }

// Export → Import → Compare
let qasm = OpenQasm.export original
let imported = OpenQasmImport.parse qasm

match imported with
| Ok circuit ->
    assert (circuit.QubitCount = original.QubitCount)
    assert (circuit.Gates.Length = original.Gates.Length)
    printfn "✅ Round-trip successful"
| Error msg -> 
    printfn "❌ Round-trip failed: %s" msg
```

### Use Cases

1. **Share algorithms** - Export F# quantum algorithms to IBM Qiskit community
2. **Import research** - Load published Qiskit papers/benchmarks into F# for analysis
3. **Multi-provider** - Develop in F#, run on IBM Quantum, Amazon Braket, IonQ
4. **Education** - Students learn quantum with type-safe F#, export to standard format
5. **Validation** - Cross-check results between F# LocalBackend and IBM simulators

**See:** `tests/OpenQasmIntegrationTests.fs` for comprehensive examples

---

## Compilation & Hardware Tooling

Beyond circuit construction and execution, the library ships a hardware-aware
compilation and estimation toolchain (all in `Builders/`):

- **`QubitRouting`** — inserts SWAPs so two-qubit gates respect a device's `CouplingMap` (grid / linear / `fromPairs`) and tracks the logical→physical qubit permutation. `CloudBackends.Factory.createRigettiRouted` wires this into a Rigetti backend automatically.
- **`NoiseModel`** — `DeviceNoiseProfile` plus noise-aware routing (`routeNoiseAware`) and success-probability estimation.
- **`ResourceEstimation`** — logical resource estimates (qubits / gates / T-count / depth) via `estimateLogical` and physical surface-code estimates via `estimatePhysical`.
- **`QirEmitter`** — emit circuits as QIR base-profile textual LLVM IR for Azure Quantum submission.
- **Weighted MAX-SAT** — per-clause weights in `QuantumSatSolver` (`clause` / `weightedClause`); solutions report `SatisfiedWeight` / `TotalWeight`.
- **`AutoML.TrainedModel`** — AutoML returns a typed `TrainedModel` discriminated union (no `obj` unboxing in `predict`).
- **`CudaQBridge`** — hand a circuit to **NVIDIA CUDA-Q** for GPU / tensor-network / density-matrix simulation. CUDA-Q has no .NET binding, so this is a *source hand-off*: `CudaQBridge.toKernelSource "nvidia" shots circuit` emits a runnable CUDA-Q Python kernel (pure, dependency-free); `CudaQBridge.runAsync` optionally executes it via a local `python`+`cudaq` and parses the counts (returns `Error`, not an exception, when CUDA-Q isn't installed).

These are exercised by the tests under `tests/` (e.g. `QubitRoutingTests`, `ResourceEstimationTests`, `QirEmitterTests`, `NoiseModelTests`).

---

## Error Mitigation

**Reduce quantum noise and improve result accuracy by 30-90% with production-ready error mitigation techniques.**

### Why Error Mitigation?

Quantum computers are noisy (NISQ era). Error mitigation improves results **without** requiring error-corrected qubits:

- **Gate errors** - Imperfect quantum gates introduce noise (~0.1-1% per gate)
- **Decoherence** - Qubits lose quantum information over time
- **Readout errors** - Measurement outcomes have ~1-5% error rate

**Error mitigation achieves near-ideal results on noisy hardware** - critical for real-world quantum advantage.

---

### Available Techniques

#### 1️⃣ Zero-Noise Extrapolation (ZNE)

**Richardson extrapolation to estimate error-free result.**

**How it works:**
1. Run circuit at different noise levels (1.0x, 1.5x, 2.0x)
2. Fit polynomial to noise vs. result
3. Extrapolate to zero noise (λ=0)

**Performance:**
- 30-50% error reduction
- 3x cost overhead (3 noise scaling levels)
- Works on any backend (IonQ, Rigetti, Local)

**F# Example:**
```fsharp
open FSharp.Azure.Quantum.ErrorMitigation

// Configure ZNE
let zneConfig = {
    NoiseScalings = [
        ZeroNoiseExtrapolation.IdentityInsertion 0.0    // baseline (1.0x)
        ZeroNoiseExtrapolation.IdentityInsertion 0.5    // 1.5x noise
        ZeroNoiseExtrapolation.IdentityInsertion 1.0    // 2.0x noise
    ]
    PolynomialDegree = 2
    MinSamples = 1000
}

// Apply ZNE to circuit expectation value
// Mock circuit and observable for demonstration
let circuit = QuantumCircuit.empty()  // Mock circuit
let observable = PauliOperator.Z(0)   // Mock observable

async {
    let! result = ZeroNoiseExtrapolation.mitigate circuit observable zneConfig backend
    
    match result with
    | Ok zneResult ->
        printfn "Zero-noise value: %f" zneResult.ZeroNoiseValue
        printfn "R² goodness of fit: %f" zneResult.GoodnessOfFit
        printfn "Measured values:"
        zneResult.MeasuredValues 
        |> List.iter (fun (noise, value) -> printfn "  λ=%.1f: %f" noise value)
    | Error msg -> 
        printfn "ZNE failed: %s" msg
}
```

**When to use:**
- Medium-depth circuits (20-50 gates)
- Cost-constrained (3x affordable)
- Need 30-50% error reduction

---

#### 2️⃣ Probabilistic Error Cancellation (PEC)

**Quasi-probability decomposition with importance sampling.**

**How it works:**
1. Decompose noisy gates into sum of ideal gates with quasi-probabilities (some negative!)
2. Sample circuits from quasi-probability distribution
3. Reweight samples to cancel noise

**Performance:**
- 2-3x accuracy improvement
- 10-100x cost overhead (Monte Carlo sampling)
- Powerful for high-accuracy requirements

**F# Example:**
```fsharp
open FSharp.Azure.Quantum.ErrorMitigation

// Configure PEC with noise model
let pecConfig = {
    NoiseModel = {
        SingleQubitDepolarizing = 0.001  // 0.1% per single-qubit gate
        TwoQubitDepolarizing = 0.01      // 1% per two-qubit gate
        ReadoutError = 0.02              // 2% readout error
    }
    Samples = 1000
    Seed = Some 42
}

// Apply PEC to circuit
async {
    let! result = ProbabilisticErrorCancellation.mitigate circuit observable pecConfig backend
    
    match result with
    | Ok pecResult ->
        printfn "Corrected expectation: %f" pecResult.CorrectedExpectation
        printfn "Uncorrected (noisy): %f" pecResult.UncorrectedExpectation
        printfn "Error reduction: %.1f%%" (pecResult.ErrorReduction * 100.0)
        printfn "Overhead: %.1fx" pecResult.Overhead
    | Error msg -> 
        printfn "PEC failed: %s" msg
}
```

**When to use:**
- High-accuracy requirements (research, benchmarking)
- Budget available for 10-100x overhead
- Need 2-3x accuracy improvement

---

#### 3️⃣ Readout Error Mitigation (REM)

**Confusion matrix calibration with matrix inversion.**

**How it works:**
1. **Calibration phase** - Prepare all basis states (|00⟩, |01⟩, |10⟩, |11⟩), measure confusion matrix
2. **Correction phase** - Invert matrix, multiply by measured histogram
3. **Result** - Corrected histogram with confidence intervals

**Performance:**
- 50-90% readout error reduction
- ~0x runtime overhead (one-time calibration, then free!)
- Works on all backends

**F# Example:**
```fsharp
open FSharp.Azure.Quantum.ErrorMitigation

// Step 1: Calibrate (one-time cost per backend)
let remConfig = 
    ReadoutErrorMitigation.defaultConfig
    |> ReadoutErrorMitigation.withCalibrationShots 10000
    |> ReadoutErrorMitigation.withConfidenceLevel 0.95

async {
    // Calibrate confusion matrix (run once, cache result)
    let! calibrationResult = ReadoutErrorMitigation.calibrate remConfig backend
    
    match calibrationResult with
    | Ok calibMatrix ->
        printfn "Calibration complete:"
        printfn "  Qubits: %d" calibMatrix.Qubits
        printfn "  Shots: %d" calibMatrix.CalibrationShots
        printfn "  Backend: %s" calibMatrix.Backend
        
        // Step 2: Correct measurement histogram (zero overhead!)
        let noisyHistogram = Map.ofList [("00", 0.9); ("01", 0.05); ("10", 0.03); ("11", 0.02)]  // Mock noisy results
        let correctedResult = ReadoutErrorMitigation.correctHistogram noisyHistogram calibMatrix remConfig
        
        match correctedResult with
        | Ok corrected ->
            printfn "\nCorrected histogram:"
            corrected.Histogram 
            |> Map.iter (fun state prob -> printfn "  |%s⟩: %.4f" state prob)
            
            printfn "\nConfidence intervals (95%%):"
            corrected.ConfidenceIntervals
            |> Map.iter (fun state (lower, upper) -> 
                printfn "  |%s⟩: [%.4f, %.4f]" state lower upper)
        | Error msg -> 
            printfn "Correction failed: %s" msg
    | Error msg -> 
        printfn "Calibration failed: %s" msg
}
```

**When to use:**
- Shallow circuits (readout errors dominate)
- Cost-constrained (free after calibration)
- All quantum applications (always beneficial)

---

#### 4️⃣ Automatic Strategy Selection

**Let the library choose the best technique for your circuit.**

**F# Example:**
```fsharp
open FSharp.Azure.Quantum.ErrorMitigation

// Define selection criteria
let criteria = {
    CircuitDepth = 25
    QubitCount = 6
    Backend = Types.Backend.IonQBackend
    MaxCostUSD = Some 10.0
    RequiredAccuracy = Some 0.95
}

// Get recommended strategy
let recommendation = ErrorMitigationStrategy.selectStrategy criteria

printfn "Recommended: %s" (
    match recommendation.Primary with
    | ZeroNoiseExtrapolation _ -> "Zero-Noise Extrapolation (ZNE)"
    | ProbabilisticErrorCancellation _ -> "Probabilistic Error Cancellation (PEC)"
    | ReadoutErrorMitigation _ -> "Readout Error Mitigation (REM)"
    | Combined _ -> "Combined Techniques"
)
printfn "Reasoning: %s" recommendation.Reasoning
printfn "Estimated cost multiplier: %.1fx" recommendation.EstimatedCostMultiplier
printfn "Estimated accuracy: %.1f%%" (recommendation.EstimatedAccuracy * 100.0)

// Apply recommended strategy
let noisyHistogram = Map.ofList [("00", 0.9); ("01", 0.05); ("10", 0.03); ("11", 0.02)]  // Mock noisy results
let mitigatedResult = ErrorMitigationStrategy.applyStrategy noisyHistogram recommendation.Primary

match mitigatedResult with
| Ok result ->
    printfn "\nMitigation successful:"
    printfn "  Technique: %A" result.AppliedTechnique
    printfn "  Used fallback: %b" result.UsedFallback
    printfn "  Actual cost: %.1fx" result.ActualCostMultiplier
    result.Histogram |> Map.iter (fun k v -> printfn "    %s: %f" k v)
| Error msg ->
    printfn "Mitigation failed: %s" msg
```

**Strategy selection logic:**
- **Shallow (depth < 20)**: Readout errors dominate → REM
- **Medium (20-50)**: Gate errors significant → ZNE
- **Deep (> 50)**: High gate errors → PEC or Combined (ZNE + REM)
- **High accuracy**: PEC (if budget allows)
- **Cost-constrained**: REM (free) or ZNE (3x)

---

### Decision Matrix

| **Circuit Type** | **Best Technique** | **Error Reduction** | **Cost** | **Why** |
|------------------|-------------------|---------------------|----------|---------|
| Shallow (< 20 gates) | REM | 50-90% | ~0x | Readout dominates |
| Medium (20-50 gates) | ZNE | 30-50% | 3x | Balanced gate/readout |
| Deep (> 50 gates) | PEC or ZNE+REM | 40-70% | 10-100x | High gate errors |
| Cost-constrained | REM | 50-90% | ~0x | Free after calibration |
| High accuracy | PEC | 2-3x | 10-100x | Research/benchmarking |

---

### Real-World Example: MaxCut with ZNE

```fsharp
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.ErrorMitigation

// Define MaxCut problem
let vertices = ["A"; "B"; "C"; "D"]
let edges = [
    ("A", "B", 1.0)
    ("B", "C", 2.0)
    ("C", "D", 1.0)
    ("D", "A", 1.0)
]

let problem = MaxCut.problem vertices edges

// Solve with ZNE error mitigation
let zneConfig = {
    NoiseScalings = [
        ZeroNoiseExtrapolation.IdentityInsertion 0.0
        ZeroNoiseExtrapolation.IdentityInsertion 0.5
        ZeroNoiseExtrapolation.IdentityInsertion 1.0
    ]
    PolynomialDegree = 2
    MinSamples = 1000
}

async {
    // Standard solve (noisy)
    let! noisyResult = MaxCut.solve problem None
    
    // Solve with ZNE (error-mitigated)
    let! mitigatedResult = MaxCut.solveWithErrorMitigation problem (Some zneConfig) None
    
    match noisyResult, mitigatedResult with
    | Ok noisy, Ok mitigated ->
        printfn "Noisy cut value: %.2f" noisy.CutValue
        printfn "ZNE-mitigated cut value: %.2f" mitigated.CutValue
        printfn "Improvement: %.1f%%" ((mitigated.CutValue - noisy.CutValue) / noisy.CutValue * 100.0)
    | _ -> 
        printfn "Error occurred"
}
```

**Expected improvement**: 30-50% better cut value on noisy hardware.

---

### Testing & Validation

Error mitigation includes comprehensive testing:

- **1804 total test cases** (1353 main + 451 topological)
  - Error Mitigation: 534 tests
    - ZNE: 111 tests (Richardson extrapolation, noise scaling, goodness-of-fit)
    - PEC: 222 tests (quasi-probability, Monte Carlo, integration)
    - REM: 161 tests (calibration, matrix inversion, confidence intervals)
    - Strategy: 40 tests (selection logic, cost estimation, fallbacks)
  - Topological: 451 tests (anyon systems, braiding, fusion)
  - Core & Algorithms: 819 tests (circuits, gates, QAOA, QFT, Grover, etc.)

**See:** `tests/FSharp.Azure.Quantum.Tests/*ErrorMitigation*.fs`

---

### Further Reading

- **ZNE Paper**: [Digital zero-noise extrapolation for quantum error mitigation (arXiv:2005.10921)](https://arxiv.org/abs/2005.10921)
- **PEC Paper**: [Probabilistic error cancellation with sparse Pauli-Lindblad models (arXiv:2201.09866)](https://arxiv.org/abs/2201.09866)
- **REM Paper**: [Practical characterization of quantum devices without tomography (arXiv:2004.11281)](https://arxiv.org/abs/2004.11281)
- **Tutorial**: [Mitiq - Quantum Error Mitigation](https://mitiq.readthedocs.io/)

---

## QAOA Algorithm Internals

### How Quantum Optimization Works

**QAOA (Quantum Approximate Optimization Algorithm):**

1. **QUBO Encoding**: Convert problem → Quadratic Unconstrained Binary Optimization
   ```
   Graph Coloring → Binary variables for node-color assignments
   MaxCut → Binary variables for partition membership
   ```

2. **Circuit Construction**: Build parameterized quantum circuit
   ```
   |0⟩^n → H^⊗n → [Cost Layer (γ)] → [Mixer Layer (β)] → Measure
   ```

3. **Parameter Optimization**: Find optimal (γ, β) using Nelder-Mead
   ```fsharp
   for iteration in 1..maxIterations do
       let cost = evaluateCost(gamma, beta)
       optimizer.Update(cost)
   ```

4. **Solution Extraction**: Decode measurement results → problem solution
   ```
   Bitstring "0101" → [R1→Red, R2→Blue, R3→Red, R4→Blue]
   ```

### QAOA Configuration

```fsharp
// Custom QAOA parameters
let quantumConfig : QuantumGraphColoringSolver.QuantumGraphColoringConfig = {
    OptimizationShots = 100        // Shots per optimization step
    FinalShots = 1000              // Shots for final measurement
    EnableOptimization = true      // Enable parameter optimization
    InitialParameters = (0.5, 0.5) // Starting (gamma, beta)
}

// Use custom config
let backend = LocalBackend() :> IQuantumBackend
match QuantumGraphColoringSolver.solve backend problem quantumConfig with
| Ok result -> printfn "Colors used: %d" result.ColorsUsed
| Error msg -> printfn "Error: %s" msg
```

---

## Execution Primitives (CUDA-Q-style)

The `Primitives` module gives every `IQuantumBackend` a small execution surface that
mirrors CUDA-Q's `sample` / `observe` / `run` / `get_state`, so code (or an agent)
written against that model maps directly onto this library:

| CUDA-Q                | FSharp.Azure.Quantum      | Returns |
|-----------------------|---------------------------|---------|
| `cudaq.sample`        | `Primitives.sample`       | `Map<string,int>` — bitstring histogram |
| `cudaq.run`           | `Primitives.run`          | `int[][]` — raw per-shot outcomes |
| `cudaq.observe`       | `Primitives.observe`      | `float` — expectation ⟨H⟩ of a Pauli Hamiltonian |
| `cudaq.get_state`     | `Primitives.getState`     | `QuantumState` — full statevector (simulator) |
| `cudaq.sample_async`  | `Primitives.sampleAsync`  | `Task<…>` |
| `cudaq.observe_async` | `Primitives.observeAsync` | `Task<…>` |

The "kernel" is a `CircuitBuilder.Circuit`; the backend is the local simulator or any
cloud QPU (IonQ, Rigetti, Quantinuum, Atom Computing, IQM). Every primitive returns a
`Result`, so a backend rejecting the circuit is a handleable `Error` rather than an exception.

```fsharp
open System.Numerics
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Backends
open FSharp.Azure.Quantum.Algorithms

let backend = LocalBackend.LocalBackend() :> IQuantumBackend

// A Bell-state "kernel"
let bell =
    CircuitBuilder.empty 2
    |> CircuitBuilder.addGate (CircuitBuilder.H 0)
    |> CircuitBuilder.addGate (CircuitBuilder.CNOT (0, 1))

Primitives.sample backend bell 1000            // Result<Map<string,int>>  → |00⟩ ~50%, |11⟩ ~50%

// observe ⟨H⟩ for a Pauli Hamiltonian (TrotterSuzuki.PauliHamiltonian: 'I'/'X'/'Y'/'Z' per qubit)
let zz : TrotterSuzuki.PauliHamiltonian =
    { Terms = [ { Operators = [| 'Z'; 'Z' |]; Coefficient = Complex(1.0, 0.0) } ]; NumQubits = 2 }
Primitives.observe backend bell zz             // Result<float>  → +1.0 for a Bell state
```

`observe` computes ⟨H⟩ for a Pauli Hamiltonian across every state representation: an exact
⟨ψ|H|ψ⟩ on a state vector, topological superposition, or sparse state, and `Tr(ρH)` on the
density-matrix (noisy) backend — so a *noisy* ⟨H⟩ (noisy VQE/VaR) just works. It returns
`Error` only for annealing samples, where an expectation value isn't defined.

**Batch / multi-QPU** — `Primitives.sampleBatchAsync` / `observeBatchAsync` run many circuits
concurrently on one backend (parameter sweeps); `sampleDistributedAsync` fans a list of
`(backend, circuit)` jobs across multiple QPUs. The library's counterpart to CUDA-Q's `mqpu`.

**Emulate a hardware target locally** (`cudaq emulate=True` counterpart) —
`Emulation.emulate "ionq.qpu.aria-1" shots circuit` transpiles the circuit to the target's
native gate set, validates it against that device's qubit-count/connectivity/gate constraints,
and runs it on the local simulator — returning the histogram plus any `ConstraintViolations`.
Catch "won't fit this device" locally before paying for a hardware job.

```fsharp
match Emulation.emulate "rigetti.qpu.aspen-m-3" 1000 circuit with
| Ok report ->
    if report.ConstraintViolations.IsEmpty then printfn "would run cleanly"
    else report.ConstraintViolations |> List.iter (printfn "  ⚠ %s")
| Error e -> eprintfn "%s" e.Message
```

**Noisy (density-matrix) simulation** — `DensityMatrixSimulator.NoisyLocalBackend` evolves a
full density matrix and applies a depolarizing channel after each gate, modelling the mixed
states real hardware produces. It's a drop-in `IQuantumBackend`, so `Primitives.sample` reads
its noisy statistics directly:

```fsharp
open FSharp.Azure.Quantum.Backends.DensityMatrixSimulator
let noisy = NoisyLocalBackend(depolarizing 0.05 0.05) :> IQuantumBackend   // 5% single/two-qubit error
Primitives.sample noisy bellCircuit 4000   // a Bell state now leaks a little into |01⟩/|10⟩
```

Intended for small circuits (≤ 8 qubits — a 2ⁿ×2ⁿ matrix). ▶ Runnable examples:
[`examples/Primitives/CudaQStylePrimitives.fsx`](examples/Primitives/CudaQStylePrimitives.fsx) ·
[`examples/ErrorMitigation/NoisyDensityMatrix.fsx`](examples/ErrorMitigation/NoisyDensityMatrix.fsx)

---

## ADAPT-VQE (adaptive ansatz)

`AdaptVqe` grows a variational ansatz one operator at a time instead of using a fixed
form: each round it screens an operator pool by the energy gradient each operator would
contribute, appends the highest-gradient operator as a new `e^(-iθP)` block, re-optimises
all angles, and stops when no pool operator has a meaningful gradient left. The result is
a compact, problem-tailored ansatz — often far shallower than a fixed hardware-efficient
form for the same accuracy.

```fsharp
open System.Numerics
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Backends
open FSharp.Azure.Quantum.Algorithms

let backend = LocalBackend.LocalBackend() :> IQuantumBackend
let term ops c : TrotterSuzuki.PauliString = { Operators = ops; Coefficient = Complex(c, 0.0) }

// H = X₀ + X₁ + ½ Z₀Z₁
let h : TrotterSuzuki.PauliHamiltonian =
    { Terms = [ term [|'X';'I'|] 1.0; term [|'I';'X'|] 1.0; term [|'Z';'Z'|] 0.5 ]; NumQubits = 2 }

// Operator pool (unit-coefficient generators) and run:
let pool = [ term [|'Y';'I'|] 1.0; term [|'I';'Y'|] 1.0; term [|'Y';'X'|] 1.0; term [|'X';'Y'|] 1.0 ]
match AdaptVqe.run backend h pool 2 AdaptVqe.defaultConfig with
| Ok result -> printfn "ground energy %.4f in %d operators" result.Energy result.SelectedOperators.Length
| Error e   -> eprintfn "%s" e.Message
```

`AdaptVqe.run` is state-vector exact (uses `Primitives.expectation` for ⟨H⟩ and gradients),
so it needs a state-vector simulator backend. `AdaptResult` reports the final `Energy`, the
`SelectedOperators`/`Parameters`, and the `EnergyHistory` (monotonically non-increasing).

**ADAPT-QAOA** (`AdaptQaoa.run`) applies the same idea to QAOA: instead of a fixed mixer it
selects, at each layer, the mixer from a pool with the largest gradient — each layer being a
cost evolution `e^(-iγH)` followed by the chosen mixer `e^(-iβA)`, starting from `|+…+⟩`. It
solves MaxCut on a frustrated triangle to the optimal `min ⟨H⟩ = -1` in a single adaptive layer.

It's wired into the business layer too: `AdaptQaoa.solveQubo backend numQubits quboMap config`
solves any QUBO end-to-end (Ising mapping → adaptive ansatz → best sampled assignment), and
**`MaxCut.solveWithAdaptQaoa problem backend`** offers ADAPT-QAOA as a drop-in alternative to the
fixed-mixer `MaxCut.solve` — same `Solution` type (partition, cut value), shallower ansatz.

▶ Runnable examples: [`examples/Algorithms/AdaptVqe.fsx`](examples/Algorithms/AdaptVqe.fsx) ·
[`examples/MaxCut/AdaptQaoaMaxCut.fsx`](examples/MaxCut/AdaptQaoaMaxCut.fsx)

---

## Documentation

- **[Quantum Computing Introduction](docs/quantum-computing-introduction.md)** - Comprehensive introduction to quantum computing for F# developers (no quantum background needed)
- **[Getting Started Guide](docs/getting-started.md)** - Installation and first examples
- **[C# Usage Guide](CSHARP-QUANTUM-BUILDER-USAGE-GUIDE.md)** - Complete C# interop guide
- **[API Reference](docs/api-reference.md)** - Complete API documentation
- **[Computation Expressions Reference](docs/computation-expressions-reference.md)** - Complete CE reference table with all custom operations (when IntelliSense fails)
- **[Architecture Overview](docs/architecture-overview.md)** - Deep dive into library design
- **[Backend Switching Guide](docs/backend-switching.md)** - Local vs Cloud backends
- **[Bring Your Own Hamiltonian](docs/bring-your-own-hamiltonian.md)** - Plug in external chemistry packages (PySCF, Psi4, FCIDUMP, fermionic/Pauli Hamiltonians)
- **[FAQ](docs/faq.md)** - Common questions and troubleshooting

---

## Problem Size Guidelines

| Problem Type | Small (LocalBackend) | Medium | Large (Cloud Required) |
|--------------|---------------------|--------|----------------------|
| **Graph Coloring** | ≤20 nodes | 20-30 nodes | 30+ nodes |
| **MaxCut** | ≤20 vertices | 20-30 vertices | 30+ vertices |
| **Knapsack** | ≤20 items | 20-30 items | 30+ items |
| **TSP** | ≤8 cities | 8-12 cities | 12+ cities |
| **Portfolio** | ≤20 assets | 20-30 assets | 30+ assets |
| **Network Flow** | ≤15 nodes | 15-25 nodes | 25+ nodes |
| **Task Scheduling** | ≤15 tasks | 15-25 tasks | 25+ tasks |

**Note:** LocalBackend limited to 20 qubits. Larger problems require Azure Quantum backends.

---

## Design Philosophy

### Rule 1: Quantum-First — no silent classical fallback

**The primary solvers are quantum, and a quantum solver never silently substitutes a
classical result.** If a quantum run cannot produce an answer it returns `Error` — it
does not quietly hand back a classical approximation dressed up as a quantum result.

```fsharp
// ✅ QUANTUM: QAOA-based optimization on a real backend
GraphColoring.solve problem 3 None

// ❌ NO SILENT FALLBACK: if the quantum path fails, you get Error — not a hidden
//    classical answer. Reach for a dedicated classical library when you want one.
```

Classical code that *does* ship is deliberately scoped and never the default product:
- **Comparison baselines** inside `Solvers/Classical` (e.g. `TspSolver`, `PortfolioSolver`) exist so you can benchmark quantum vs classical on the same problem.
- **`HybridSolver`** can *explicitly* route to a classical solver (and reports `Method = Classical`) when you ask it to, or when the `QuantumAdvisor` judges a problem too small to benefit from quantum. This is an opt-in router, not a fallback hidden inside a quantum solver.

Every business builder and quantum solver defaults to a **real quantum backend** — the local simulator when you don't pass one, any cloud backend (IonQ / Rigetti / Quantinuum / Atom Computing / D-Wave) when you do. There is no "classical mode" of the quantum solvers.

### Two design decisions worth knowing

**Errors: `Result` for business, `failwith` for technical.** Expected, domain-level
outcomes (invalid input, a backend rejecting a circuit, an unsupported problem shape)
are returned as `Result<_, QuantumError>` so callers can handle them. Programmer errors
and broken invariants (a state in the wrong representation, a violated precondition)
fail fast with `failwith`/exceptions. `QuantumError` is a discriminated union
(`Core/QuantumError.fs`) precisely so business errors are enumerable rather than stringly-typed.

**Low-level algorithm modules without a high-level builder are intentional, not dead code.**
Foundational algorithms (QFT, amplitude amplification, arithmetic, HHL, …) are part of
the public API even where no domain "builder" wraps them yet — a builder is only added
once a concrete business case justifies the abstraction, rather than speculatively.

### Clean API Layers

1. **High-Level Builders**: Business domain APIs (register allocation, portfolio optimization)
2. **Quantum Solvers**: QAOA implementations (algorithm experts)
3. **Quantum Backends**: Circuit execution (hardware abstraction)

**No leaky abstractions** - Each layer has clear responsibilities.

---

## Quantum Algorithms

In addition to the optimization solvers above, the library includes **foundational quantum algorithms** for education and research:

### Grover's Search Algorithm

**Quantum search algorithm for finding elements in unsorted databases.**

```fsharp
open FSharp.Azure.Quantum.GroverSearch
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Backends.LocalBackend

// Search an 8-element (3-qubit) space for items where the predicate holds
let predicate x = x = 3 || x = 5            // Looking for values 3 or 5
let backend = LocalBackend() :> IQuantumBackend
let config = { Grover.defaultConfig with Shots = 1000; RandomSeed = Some 42 }

// Compile a predicate oracle, then run Grover search on the backend
match Oracle.fromPredicate predicate 3 with
| Ok oracle ->
    match Grover.search oracle backend config with
    | Ok result ->
        printfn "Found solutions: %A" result.Solutions
        printfn "Success probability: %.2f%%" (result.SuccessProbability * 100.0)
        printfn "Iterations: %d" result.Iterations
    | Error err -> printfn "Search failed: %A" err
| Error err -> printfn "Oracle build failed: %A" err
```

**Features:**
- Automatic optimal iteration calculation
- Amplitude amplification for multiple solutions
- Direct LocalSimulator integration (no IBackend)
- Educational/research tool (not production optimizer)

**Location:** `src/FSharp.Azure.Quantum/Algorithms/`  
**Status:** Experimental - Research and education purposes

**Note:** Grover's algorithm is a standalone quantum search primitive, separate from the QAOA-based optimization builders. It does not use the `IBackend` abstraction and is optimized for specific search problems rather than general combinatorial optimization.

---

### Amplitude Amplification

**Generalization of Grover's algorithm for custom initial states.**

Amplitude amplification extends Grover's algorithm to work with arbitrary initial state preparations (not just uniform superposition). This enables quantum speedups for problems beyond simple database search.

**Key Insight:** Grover's algorithm is a special case where:
- Initial state = uniform superposition H^⊗n|0⟩
- Reflection operator = Grover diffusion operator

Amplitude amplification allows:
- Custom initial state preparation A|0⟩
- Reflection about A|0⟩ (automatically generated)

**Example** (▶ runnable: [`examples/Algorithms/AmplitudeAmplification.fsx`](examples/Algorithms/AmplitudeAmplification.fsx)):
```fsharp
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.GroverSearch
open FSharp.Azure.Quantum.Backends
open FSharp.Azure.Quantum.Core.BackendAbstraction

// Any IQuantumBackend — local simulator here, or a cloud backend for hardware.
let backend = LocalBackend.LocalBackend() :> IQuantumBackend

// State preparation A = H^⊗n (uniform superposition) as a gate circuit.
let prep =
    [ 0 .. 2 ] |> List.map CircuitBuilder.H
    |> List.fold (fun c g -> CircuitBuilder.addGate g c) (CircuitBuilder.empty 3)

match Oracle.forValue 5 3 with              // mark the basis state |101⟩ (value 5)
| Error e -> eprintfn "%s" e.Message
| Ok oracle ->
    let intent : AmplitudeAmplification.Unified.AmplitudeAmplificationIntent =
        { NumQubits = 3
          StatePreparation = prep
          Oracle = oracle
          Iterations = AmplitudeAmplification.optimalIterations 8 1 (1.0 / 8.0)
          Exactness = AmplitudeAmplification.Unified.Exact }

    match AmplitudeAmplification.Unified.execute backend intent with   // Result<QuantumState, _>
    | Ok finalState ->
        UnifiedBackend.measureState finalState 1000
        |> Array.countBy (fun bits -> bits |> Array.map string |> String.concat "")
        |> Array.iter (fun (state, count) -> printfn "  |%s⟩: %d counts" state count)
    | Error e -> eprintfn "Execution failed: %s" e.Message
```

**Features:**
- Custom state preparation (W-states, partial superpositions, arbitrary states)
- Cloud backend execution (IonQ, Rigetti, LocalBackend)
- Automatic reflection operator generation (circuit-based A†)
- Grover equivalence verification (shows Grover as special case)
- Optimal iteration calculation for arbitrary initial success probability
- Measurement-based results (histogram of basis states)

**Backend Limitations:**
- Backend execution returns measurement statistics only (not full amplitudes)
- Quantum phases are lost during measurement (fundamental limitation)
- Suitable for algorithms that measure amplification results
- For amplitude/phase analysis, use local simulation

**Use Cases:**
- Quantum walk algorithms with non-uniform initial distributions
- Fixed-point search (where initial state biases toward solutions)
- Quantum sampling with amplification
- Fixed-amplitude search (building block for quantum counting)

**Location:** `Algorithms/AmplitudeAmplification.fs` (`AmplitudeAmplification.Unified.execute`)

**Status:** Well-tested and documented

---

### Quantum Fourier Transform (QFT)

**Quantum analog of the discrete Fourier transform - foundational building block for many quantum algorithms.**

The QFT transforms computational basis states into frequency basis with exponential speedup over classical FFT:
- **Classical FFT**: O(n·2^n) operations
- **Quantum QFT**: O(n²) quantum gates

**Mathematical Transform:**
```
QFT: |j⟩ → (1/√N) Σₖ e^(2πijk/N) |k⟩
```

**Example** (▶ runnable: [`examples/Algorithms/QuantumFourierTransform.fsx`](examples/Algorithms/QuantumFourierTransform.fsx)):
```fsharp
open FSharp.Azure.Quantum.Algorithms
open FSharp.Azure.Quantum.Backends
open FSharp.Azure.Quantum.Core.BackendAbstraction

// Any IQuantumBackend — local simulator here, or a cloud backend for hardware.
let backend = LocalBackend.LocalBackend() :> IQuantumBackend

// Forward QFT on |0…0⟩ (5 qubits). QFT.defaultConfig = { ApplySwaps = true; Inverse = false; Shots = 1000 }.
match QFT.execute 5 backend QFT.defaultConfig with
| Ok result ->
    printfn "QFT applied: %d gates" result.GateCount           // result.FinalState : QuantumState
    UnifiedBackend.measureState result.FinalState 1000
    |> Array.countBy (fun bits -> bits |> Array.map string |> String.concat "")
    |> Array.iter (fun (state, count) -> printfn "  |%s⟩: %d counts" state count)
| Error e -> eprintfn "QFT failed: %s" e.Message

// Variants:
//   QFT.executeInverse 5 backend                                   // inverse QFT (QFT†)
//   QFT.executeNoSwaps 5 backend                                   // omit bit-reversal SWAPs (for QPE)
//   QFT.executeOnState state backend { QFT.defaultConfig with Inverse = true }   // apply to an existing state
```

**Features:**
- O(n²) gate complexity (exponential speedup over classical)
- Cloud backend execution (IonQ, Rigetti, LocalBackend)
- Controlled phase gates (CP) with correct decomposition
- Bit-reversal SWAP gates (optional for QPE)
- Inverse QFT (QFT†) for result decoding
- Angle validation (prevents NaN/infinity)
- Measurement-based results (histogram of basis states)

**Backend Limitations:**
- Backend execution returns measurement statistics only (not full state vector)
- Amplitudes and phases are lost during measurement (fundamental quantum limitation)
- Suitable for algorithms that measure QFT output (Shor's, Phase Estimation)
- For amplitude/phase analysis, use local simulation

**Use Cases:**
- **Shor's Algorithm**: Integer factorization (period finding step)
- **Quantum Phase Estimation**: Eigenvalue estimation for VQE improvements
- **Period Finding**: Hidden subgroup problems
- **Quantum Signal Processing**: Frequency domain analysis

**Performance:**
- 3 qubits: 12 gates (3 H + 6 CPhase + 1 SWAP)
- 5 qubits: 27 gates (5 H + 20 CPhase + 2 SWAP)
- 10 qubits: 105 gates (10 H + 90 CPhase + 5 SWAP)

**Location:** `Algorithms/QFT.fs` (`QFT.execute` / `executeOnState` / `executeInverse` / `executeNoSwaps`)

**Status:** Well-tested and documented

---

### Phase 2 Builders: QFT-Based Quantum Applications

**High-level builders for cryptography, quantum chemistry, and research applications.**

The library provides three advanced builders that wrap QFT-based quantum algorithms for real-world business scenarios:

### Quantum Arithmetic Builder

**Use Case:** Cryptographic operations, RSA encryption, modular arithmetic

```fsharp
open FSharp.Azure.Quantum.QuantumArithmeticOps

// RSA encryption: m^e mod n
let encryptOp = quantumArithmetic {
    operands 5 3           // message=5, exponent=3
    operation ModularExponentiate
    modulus 33             // RSA modulus
    qubits 8
}

match execute encryptOp with
| Ok result -> 
    printfn "Encrypted: %d" result.Value
    printfn "Gates: %d, Depth: %d" result.GateCount result.CircuitDepth
| Error msg -> 
    printfn "Error: %s" msg
```

**C# API:**
```csharp
using static FSharp.Azure.Quantum.CSharpBuilders;

var encrypt = ModularExponentiate(baseValue: 5, exponent: 3, modulus: 33);
var result = ExecuteArithmetic(encrypt);
```

**Business Applications:**
- Cryptographic algorithm prototyping
- Educational RSA demonstrations
- Quantum arithmetic research

**Example:** [`examples/QuantumArithmetic/`](examples/QuantumArithmetic/)

---

### Cryptographic Analysis Builder (Shor's Algorithm)

**Use Case:** RSA security assessment, post-quantum cryptography planning

```fsharp
open FSharp.Azure.Quantum.QuantumPeriodFinder

// Factor RSA modulus (security analysis)
let problem = periodFinder {
    number 15              // Composite to factor
    precision 8            // QPE precision
    maxAttempts 10         // Retries (probabilistic)
}

match solve problem with
| Ok result ->
    match result.Factors with
    | Some (p, q) -> 
        printfn "Factors: %d × %d" p q
        printfn "⚠️  RSA Security Broken!"
    | None -> 
        printfn "Try again (probabilistic)"
| Error msg -> 
    printfn "Error: %s" msg
```

**C# API:**
```csharp
var problem = FactorInteger(15, precision: 8);
var result = ExecutePeriodFinder(problem);
```

**Business Applications:**
- Security consulting (quantum threat assessment)
- Post-quantum cryptography migration planning
- Cryptanalysis research and education

**Example:** [`examples/CryptographicAnalysis/`](examples/CryptographicAnalysis/)

---

### Phase Estimation Builder (Quantum Chemistry)

**Use Case:** Drug discovery, molecular simulation, materials science

```fsharp
open FSharp.Azure.Quantum.QuantumPhaseEstimator

// Estimate molecular ground state energy
let problem = phaseEstimator {
    unitary (RotationZ (Math.PI / 3.0))  // Molecular Hamiltonian
    precision 12                          // 12-bit energy precision
}

match estimate problem with
| Ok result ->
    printfn "Phase: %.6f" result.Phase
    printfn "Energy: %.4f a.u." (result.Phase * 2.0 * Math.PI)
    printfn "Application: Drug binding affinity prediction"
| Error msg ->
    printfn "Error: %s" msg
```

**C# API:**
```csharp
var problem = EstimateRotationZ(Math.PI / 3.0, precision: 12);
var result = ExecutePhaseEstimator(problem);
```

**Business Applications:**
- Pharmaceutical drug discovery (binding energy)
- Materials science (electronic properties)
- Quantum chemistry simulations

**Example:** [`examples/PhaseEstimation/`](examples/PhaseEstimation/)

---

**Phase 2 Builder Features:**
- Business-focused APIs hiding quantum complexity
- F# computation expressions + C# fluent API
- Comprehensive examples with real-world scenarios
- Educational value for quantum algorithm learning
- NISQ limitations: Toy examples only (< 20 qubits)
- Requires fault-tolerant quantum computers for production use

**Current Status:** Educational/research focus - Demonstrates quantum algorithms but hardware insufficient for real-world applications (as of 2025)

---

| **Feature Category** | **FSharp.Azure.Quantum** | **IBM Qiskit** | **Microsoft Azure Quantum SDK** | **Google Cirq** | **Amazon Braket SDK** |
|---------------------|-------------------------|----------------|-------------------------------|----------------|---------------------|
| **Primary Language** | F# (with C# interop) | Python | Python, C#, Q# | Python | Python |
| **License** | Unlicense (Public Domain) | Apache 2.0 | MIT | Apache 2.0 | Apache 2.0 |
| **Target Audience** | .NET developers, optimization problems | General quantum computing | Enterprise quantum developers | Google hardware users | AWS cloud users |
| | | | | | |
| **🎯 OPTIMIZATION SOLVERS** | | | | | |
| MaxCut | ✅ Built-in (QAOA) | ✅ Qiskit Optimization | ❌ Manual | ❌ Manual | ❌ Manual |
| Knapsack | ✅ Built-in (QAOA) | ✅ Qiskit Optimization | ❌ Manual | ❌ Manual | ❌ Manual |
| TSP | ✅ Built-in (QAOA) | ✅ Qiskit Optimization | ❌ Manual | ❌ Manual | ❌ Manual |
| Portfolio Optimization | ✅ Built-in (QAOA) | ✅ Qiskit Finance | ❌ Manual | ❌ Manual | ❌ Manual |
| Task Scheduling | ✅ Built-in (QAOA) | ❌ Manual | ❌ Manual | ❌ Manual | ❌ Manual |
| Network Flow | ✅ Built-in (QAOA) | ❌ Manual | ❌ Manual | ❌ Manual | ❌ Manual |
| Graph Coloring | ✅ Built-in (QAOA) | ❌ Manual implementation | ❌ Manual | ❌ Manual | ❌ Manual |
| | | | | | |
| **🤖 QUANTUM MACHINE LEARNING** | | | | | |
| VQC (Variational Classifier) | ✅ Built-in | ✅ Qiskit Machine Learning | ❌ Manual | ✅ TFQ integration | ✅ Built-in |
| Quantum Kernel SVM | ✅ Built-in | ✅ Qiskit Machine Learning | ❌ Manual | ❌ Limited | ✅ Built-in |
| Feature Maps | ✅ ZZ, Pauli, Angle | ✅ Extensive library | ❌ Manual | ✅ Via TFQ | ✅ Built-in |
| Variational Forms | ✅ RealAmplitudes, EfficientSU2 | ✅ Extensive ansatz library | ❌ Manual | ✅ Via Cirq | ✅ Built-in |
| AutoML Integration | ✅ Built-in | ❌ External tools | ❌ No | ❌ No | ❌ No |
| | | | | | |
| **📊 BUSINESS PROBLEM BUILDERS** | | | | | |
| Anomaly Detection | ✅ Built-in | ❌ Manual | ❌ No | ❌ No | ❌ No |
| Binary Classification | ✅ Built-in | ✅ Qiskit ML | ❌ No | ❌ No | ❌ No |
| Predictive Modeling | ✅ Built-in | ❌ Manual | ❌ No | ❌ No | ❌ No |
| Similarity Search | ✅ Built-in | ❌ Manual | ❌ No | ❌ No | ❌ No |
| | | | | | |
| **🔬 QUANTUM ALGORITHMS** | | | | | |
| QAOA | ✅ Production-ready, auto-optimized | ✅ Qiskit Optimization | ✅ Q# samples | ✅ Manual | ✅ Built-in |
| VQE | ✅ Built-in (chemistry) | ✅ Qiskit Nature | ✅ Q# samples | ✅ Built-in | ✅ Built-in |
| Grover's Algorithm | ✅ Educational | ✅ Built-in | ✅ Q# samples | ✅ Built-in | ✅ Built-in |
| Shor's Algorithm | ✅ Educational (period finder) | ✅ Built-in | ✅ Q# samples | ✅ Built-in | ✅ Built-in |
| QFT | ✅ Built-in | ✅ Built-in | ✅ Q# built-in | ✅ Built-in | ✅ Built-in |
| HHL (Linear Systems) | ✅ Built-in | ✅ Qiskit Aqua | ❌ Manual | ❌ Manual | ❌ Manual |
| Amplitude Amplification | ✅ Built-in | ✅ Built-in | ✅ Q# built-in | ✅ Built-in | ❌ Manual |
| | | | | | |
| **🖥️ LOCAL SIMULATION** | | | | | |
| Local Simulator | ✅ Built-in (≤20 qubits) | ✅ Aer (≤30 qubits) | ✅ Full-state (≤30 qubits) | ✅ Built-in (≤20 qubits) | ✅ Local simulator |
| Noise Simulation | ❌ Some | ✅ AerSimulator noise models | ✅ Open/Closed systems | ✅ Built-in | ✅ Built-in |
| GPU Acceleration | ❌ No | ✅ Aer GPU | ✅ Yes | ✅ Yes | ✅ Yes |
| State Vector | ✅ Pure F# implementation | ✅ C++ backend | ✅ C++ backend | ✅ C++ backend | ✅ C++ backend |
| | | | | | |
| **☁️ CLOUD BACKENDS** | | | | | |
| Azure Quantum (IonQ) | ✅ Native | ✅ Via Qiskit Runtime | ✅ Native | ❌ No | ❌ No |
| Azure Quantum (Rigetti) | ✅ Native | ✅ Via Qiskit Runtime | ✅ Native | ❌ No | ❌ No |
| IBM Quantum | ❌ Via OpenQASM export | ✅ Native | ❌ No | ❌ No | ❌ No |
| D-Wave Quantum Annealer | ✅ Native | ✅ Via Ocean SDK | ✅ Native | ❌ No | ✅ Native |
| AWS Braket | ❌ Via OpenQASM export | ✅ Via plugin | ❌ No | ❌ No | ✅ Native |
| Google Quantum | ❌ Via OpenQASM export | ✅ Via plugin | ❌ No | ✅ Native | ❌ No |
| | | | | | |
| **🔄 INTEROPERABILITY** | | | | | |
| OpenQASM 2.0 Import | ✅ Full support | ✅ Native | ✅ Via conversion | ✅ Full support | ✅ Full support |
| OpenQASM 2.0 Export | ✅ Full support | ✅ Native | ✅ Via conversion | ✅ Full support | ✅ Full support |
| QUIL | ❌ No | ❌ Via plugin | ✅ Rigetti native | ❌ No | ✅ Rigetti support |
| | | | | | |
| **🛡️ ERROR MITIGATION** | | | | | |
| Zero-Noise Extrapolation | ✅ Built-in (30-50% reduction) | ✅ Qiskit Experiments | ❌ Manual | ✅ Via Mitiq integration | ❌ Manual |
| Probabilistic Error Cancellation | ✅ Built-in (2-3x accuracy) | ✅ Via Mitiq | ❌ Manual | ✅ Via Mitiq integration | ❌ Manual |
| Readout Error Mitigation | ✅ Built-in (50-90% reduction) | ✅ Qiskit Experiments | ❌ Manual | ✅ Via Mitiq integration | ❌ Manual |
| Automatic Strategy Selection | ✅ Built-in | ❌ Manual | ❌ No | ❌ Manual | ❌ No |
| | | | | | |
| **💻 API DESIGN** | | | | | |
| Computation Expressions | ✅ F# native pattern | ❌ N/A (Python) | ❌ No | ❌ N/A (Python) | ❌ N/A (Python) |
| Type Safety | ✅ F# compile-time checks | ⚠️ Python dynamic typing | ⚠️ Python/C# mixed | ⚠️ Python dynamic typing | ⚠️ Python dynamic typing |
| Fluent API (C#) | ✅ Built-in | ❌ N/A | ✅ Native C# | ❌ N/A | ❌ N/A |
| Functional Programming | ✅ F# first-class | ❌ Object-oriented | ⚠️ Mixed | ⚠️ Mixed | ❌ Object-oriented |
| Result Type Error Handling | ✅ F# Result<T,E> | ❌ Exceptions | ❌ Exceptions | ❌ Exceptions | ❌ Exceptions |
| | | | | | |
| **🤖 HYBRID CLASSICAL-QUANTUM** | | | | | |
| Automatic Problem Routing | ✅ HybridSolver (optional) | ❌ Manual | ❌ Manual | ❌ Manual | ❌ Manual |
| Classical Fallback | ✅ Built-in (small problems) | ❌ Manual | ❌ No | ❌ No | ❌ No |
| Cost Guards | ✅ MaxCostUSD limits | ❌ Manual | ❌ Manual | ❌ Manual | ❌ Manual |
| Quantum Advantage Analysis | ✅ Built-in reasoning | ❌ Manual | ❌ No | ❌ No | ❌ No |
| | | | | | |
| **🧪 QUANTUM CHEMISTRY** | | | | | |
| VQE for Molecules | ✅ Built-in (H₂, H₂O) | ✅ Qiskit Nature | ✅ Q# Chemistry | ✅ OpenFermion integration | ✅ Built-in |
| Hamiltonian Construction | ✅ Built-in | ✅ Qiskit Nature | ✅ Broombridge format | ✅ OpenFermion | ✅ OpenFermion |
| UCC Ansatz | ✅ Built-in | ✅ Qiskit Nature | ✅ Q# Chemistry | ✅ OpenFermion | ✅ Built-in |
| | | | | | |
| **📚 ECOSYSTEM** | | | | | |
| Circuit Visualization | ❌ Mermaid, or Export to OpenQASM → Qiskit | ✅ Native (matplotlib) | ✅ Q# visualizer | ✅ Native (matplotlib) | ✅ Native (matplotlib) |
| Documentation Quality | ✅ Comprehensive (MD docs) | ✅ Extensive (tutorials) | ✅ Microsoft Docs | ✅ Google Docs | ✅ AWS Docs |
| Example Projects | ✅ 30+ working examples | ✅ 100+ tutorials | ⚠️ Limited examples | ✅ 50+ tutorials | ✅ 40+ examples |
| Community Size | ⚠️ Small (new library) | ✅ Large (6.7k stars) | ⚠️ Medium | ✅ Medium (Google) | ⚠️ Medium |
| GitHub Stars | ⚠️ New project | ✅ 6,700+ | ⚠️ Not standalone repo | ✅ 4,000+ | ✅ 800+ |
| Stack Overflow Support | ⚠️ Limited (F# quantum niche) | ✅ Extensive | ⚠️ Medium | ⚠️ Medium | ⚠️ Limited |
| | | | | | |
| **🔧 DEVELOPMENT EXPERIENCE** | | | | | |
| IDE Support | ✅ Visual Studio, VS Code | ✅ Jupyter, VS Code | ✅ Visual Studio, VS Code | ✅ Jupyter, VS Code | ✅ Jupyter, VS Code |
| REPL/Interactive | ✅ F# Interactive (FSI) | ✅ Jupyter Notebooks | ✅ Q# Jupyter | ✅ Jupyter Notebooks | ✅ Jupyter Notebooks |
| Package Manager | ✅ NuGet | ✅ pip | ✅ NuGet, pip | ✅ pip | ✅ pip |
| Installation | ✅ dotnet add package | ✅ pip install qiskit | ✅ pip install azure-quantum | ✅ pip install cirq | ✅ pip install amazon-braket-sdk |
| | | | | | |
| **⚡ PERFORMANCE** | | | | | |
| Small Problems (<10 qubits) | ✅ LocalBackend (ms) | ✅ Aer (ms) | ✅ Full-state (ms) | ✅ Cirq (ms) | ✅ Local (ms) |
| Medium Problems (10-20 qubits) | ✅ LocalBackend (<1s) | ✅ Aer (<1s) | ✅ Full-state (<1s) | ✅ Cirq (<10s) | ✅ Local (<1s) |
| Large Problems (20+ qubits) | ⚠️ Cloud required | ✅ Aer GPU (30 qubits) | ✅ Cloud | ⚠️ Cloud required | ✅ Cloud |
| | | | | | |
| **💰 COST** | | | | | |
| Local Development | ✅ Free | ✅ Free | ✅ Free | ✅ Free | ✅ Free |
| Cloud QPU Access | 💰 Azure Quantum pricing | 💰 IBM Quantum pricing | 💰 Azure Quantum pricing | 💰 Google Quantum pricing | 💰 AWS Braket pricing |
| D-Wave Quantum | 💰 ~-10/run | 💰 Via Ocean SDK | 💰 Azure marketplace | ❌ N/A | 💰 AWS Braket |

---

## Topological Quantum Computing

**NEW:** Simulate topological quantum computers using anyon braiding - the approach behind Microsoft's Majorana quantum computing program.

Unlike gate-based quantum computing (which uses qubits and gates), topological quantum computing encodes information in **anyons** (exotic quasiparticles) and performs operations by **braiding** their worldlines. This provides inherent fault-tolerance through **topological protection**.

### Quick Example: Ising Anyons (Microsoft Majorana)

```fsharp
open FSharp.Azure.Quantum.Topological

// Create backend for Ising anyons (Microsoft's approach)
let backend = TopologicalUnifiedBackendFactory.createIsing 10

// Create entangled state via braiding
let! result = topological backend {
    // Initialize 4 sigma anyons
    do! TopologicalBuilder.initialize AnyonSpecies.AnyonType.Ising 4
    
    // Braiding creates entanglement geometrically
    do! TopologicalBuilder.braid 0  // Braid anyons 0-1
    do! TopologicalBuilder.braid 2  // Braid anyons 2-3
    
    // Measure fusion outcome
    let! (outcome, _) = TopologicalBuilder.measure 0
    return outcome
}

match result with
| Ok particle ->
    printfn "Fusion outcome: %A" particle  // Vacuum or Psi
| Error err ->
    printfn "Error: %s" err.Message
```

### Key Concepts

**Anyons:**
- Ising anyons: `{1 (vacuum), σ (sigma), ψ (psi)}` - Microsoft's Majorana approach
- Fibonacci anyons: `{1, τ}` - Theoretical universal braiding
- Fusion rule for Ising: `σ × σ = 1 + ψ` (creates quantum superposition)

**Operations:**
- **Braiding**: Exchange anyons (replaces quantum gates)
- **Fusion**: Measurement (collapses superposition to classical outcome)
- **F-moves**: Change fusion tree basis (advanced)
- **Error Correction**: Toric code (MWPM), surface codes (planar, color), anyonic charge correction

**Advantages:**
- Topological protection: Error rates ~10⁻¹² (vs 10⁻³ for gate-based)
- Passive error correction: Immunity to local noise
- Scalability: Potentially 1:1 physical-to-logical qubit ratio

**Comparison: Gate-Based vs Topological**

| Aspect | Gate-Based QC | Topological QC |
|--------|---------------|----------------|
| State | Qubit amplitudes | Fusion trees |
| Operations | H, CNOT, RZ gates | Braid, Measure |
| Error Correction | Active (surface codes) | Passive (topology) |
| Hardware | IonQ, Rigetti, IBM | Microsoft Majorana (experimental) |

### Examples

See `examples/Topological/` for complete examples:
- **BasicFusion.fsx** - Fusion rules and statistics
- **BellState.fsx** - Creating entanglement via braiding
- **BackendComparison.fsx** - Ising vs Fibonacci anyons

### Documentation

- **Getting Started**: `docs/topological/getting-started.md` (install, build, first computation)
- **Documentation Index**: `docs/topological/index.md` (full guide with learning paths)
- **Library README**: `src/FSharp.Azure.Quantum.Topological/README.md`
- **Examples**: `examples/Topological/` (10 runnable `.fsx` scripts)
- **Format Spec**: `docs/topological-format-spec.md` (import/export)

---

## Neutral-Atom (Rydberg) Analog Mode

A third machine paradigm alongside gate and topological: neutral-atom devices are programmed
as an **analog** time evolution rather than a gate circuit. You place atoms at positions and
drive them with a global pulse (Rabi frequency Ω, detuning Δ); the van-der-Waals interaction
produces the **Rydberg blockade** (nearby atoms can't both be excited), which makes these
machines natively good at **Maximum Independent Set**.

`Algorithms.NeutralAtom` fits this into the unified model the same way `BraidToGate` fits
topological braids — it **Trotterizes the analog Hamiltonian into a gate circuit** (drive →
`RX`, detuning → `P`, interaction → `CP`), so a Rydberg program runs on *any* `IQuantumBackend`.

```fsharp
open FSharp.Azure.Quantum.Algorithms.NeutralAtom
// 3-atom path A–B–C; neighbours blockade, so the max independent set is {A, C} = "101".
let register = [ { X = 0.0; Y = 0.0 }; { X = 1.0; Y = 0.0 }; { X = 2.0; Y = 0.0 } ]
let program = maximumIndependentSetProgram register 30.0 1.0 3.0 12.0   // adiabatic detuning sweep
NeutralAtom.simulate backend program 120 4000   // Result<Map<string,int>> — peaks on "101"
```

Small registers only (it Trotterizes onto the state-vector simulator). ▶ Runnable example:
[`examples/NeutralAtom/RydbergMaxIndependentSet.fsx`](examples/NeutralAtom/RydbergMaxIndependentSet.fsx)

**Run on real neutral-atom hardware — Pasqal.** The *same* `RydbergProgram` can be submitted
natively to **Pasqal** on Azure Quantum: `Algorithms.Pasqal` compiles it to a **Pulser**
abstract-representation sequence (`Pasqal.toPulserJson`) and submits it to a Pasqal target
(`Pasqal.submitAndWaitForResultsAsync httpClient workspaceUrl program shots "pasqal.qpu.fresnel"`).
So one analog program has three execution paths — Trotterized onto any gate backend for local
simulation, native analog execution on **Pasqal** (Azure Quantum, via Pulser), or native analog
execution on **QuEra Aquila** (AWS Braket, via `QuEra.toAhsProgram` → a `braket.ir.ahs.program`
AHS sequence; `QuEra.parseAhsResult` reads the results back).

### AWS Braket — a separate plugin (`FSharp.Azure.Quantum.Braket`)

The core package stays **Azure-first and AWS-SDK-free**: it only produces the *formats* Braket
consumes — **OpenQASM 3.0** for gate devices (`OpenQasm.exportV3`) and **AHS** for QuEra
(`QuEra.toAhsProgram`), both pure and testable. The actual submission lives in a separate
`FSharp.Azure.Quantum.Braket` package (the only one that references `AWSSDK.Braket`):

- **`BraketExecution.BraketBackend`** — a gate `IQuantumBackend` that submits OpenQASM 3.0 to any
  Braket gate device by ARN: **IonQ, Rigetti, IQM, OQC, Infleqtion**, and the SV1/DM1/TN1
  simulators (`Braket.Devices.*`).
- **`BraketExecution.submitAhsAsync`** — submits a neutral-atom `RydbergProgram` to **QuEra Aquila**.

So adding the AWS SDK is opt-in (reference the plugin); users who only need Azure never pull it in.

`NeutralAtom.solveMaximumIndependentSet` runs the whole thing end-to-end and returns the best
independent set as atom indices. Beyond optimisation, the module also does **analog quantum
simulation**: `NeutralAtom.quench` builds a sudden constant-drive pulse, `NeutralAtom.evolve`
returns the final state, and `NeutralAtom.rydbergDensities` reads each atom's occupation ⟨nᵢ⟩ —
so you can watch Rabi and blockade dynamics (a single atom traces ⟨n⟩ = sin²(Ωt/2); a blockaded
pair's total excitation is capped). `NeutralAtom.optimizeAnalog` closes the loop with
**variational pulse shaping** (analog QAOA): tune pulse knobs to minimise ⟨H⟩, reusing the
shared optimiser — the analog counterpart of variational gate optimisation.

### Annealing is a first-class path too

Because D-Wave backends implement `IQuantumBackend` (they reverse-extract the QUBO from the
circuit and anneal it), a QUBO problem targets **annealing hardware** through the *same* unified
API — just pass a D-Wave backend:

```fsharp
open FSharp.Azure.Quantum.Backends.DWaveBackend
open FSharp.Azure.Quantum.Backends.DWaveTypes
let annealer = MockDWaveBackend(Advantage_System6_1, seed = 42) :> IQuantumBackend
MaxCut.solve triangle (Some annealer)   // solved by simulated/real annealing, not QAOA gates
```

---

## Contributing

Contributions welcome! 

**Development principles:**
- Maintain quantum-only architecture (no classical algorithms)
- Follow F# coding conventions
- Provide C# interop for new builders
- Include comprehensive tests
- Document QAOA encodings for new problem types

---

## License

**Unlicense** - Public domain. Use freely for any purpose.

---

## Support

- **Documentation**: [docs/](docs/)
- **Issues**: [GitHub Issues](https://github.com/thorium/FSharp.Azure.Quantum/issues)
- **Examples**: [examples/](examples/)

---

**Status**: Quantum-first architecture, 7 problem builders, full QAOA implementation

