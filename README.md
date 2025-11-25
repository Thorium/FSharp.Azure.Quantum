# FSharp.Azure.Quantum

**F# library for quantum-inspired optimization** - Solve TSP, Portfolio, and combinatorial optimization problems with automatic quantum vs classical routing.

[![NuGet](https://img.shields.io/nuget/v/FSharp.Azure.Quantum.svg)](https://www.nuget.org/packages/FSharp.Azure.Quantum/)
[![License](https://img.shields.io/badge/license-Unlicense-blue.svg)](LICENSE)

## ✨ Status: Beta (v0.5.0) - Quantum Backends Ready

**Current Features (v0.5.0-beta):**
- ✅ Production-ready classical optimization (TSP, Portfolio)
- ✅ Quantum Advisor (recommendations for quantum advantage)
- ✅ **Azure Quantum backend integration** (IonQ, Rigetti simulators)
- ✅ **HybridSolver with automatic quantum routing**
- ✅ Job submission, polling, and result parsing
- ✅ Local quantum simulation (≤10 qubits)

**Ready for Integration Testing:**
- IonQ simulator (`ionq.simulator`)
- Rigetti QVM simulator (`rigetti.sim.qvm`)
- Azure authentication via Azure.Identity
- Cost limit enforcement and error handling

**Coming in v1.0:**
- QUBO-to-circuit conversion for TSP/Portfolio problems
- Quantum hardware execution (IonQ QPU, Rigetti Aspen)
- Advanced result comparison and quantum advantage validation

## 🚀 Quick Start

```fsharp
open FSharp.Azure.Quantum.Classical

// Solve a TSP problem with named cities
let cities = [
    ("Seattle", 47.6, -122.3)
    ("Portland", 45.5, -122.7)
    ("San Francisco", 37.8, -122.4)
]

match TSP.solveDirectly cities None with
| Ok tour -> 
    printfn "Best route: %A" tour.Cities
    printfn "Distance: %.2f miles" tour.TotalDistance
| Error msg -> printfn "Error: %s" msg
```

## 📦 Installation

**NuGet Package:** [FSharp.Azure.Quantum](https://www.nuget.org/packages/FSharp.Azure.Quantum)

```bash
dotnet add package FSharp.Azure.Quantum --prerelease
```

## ✨ Features

### 🔀 HybridSolver - Automatic Quantum/Classical Routing
Automatically chooses the best solver (quantum or classical) based on problem characteristics:

```fsharp
// Let the solver decide automatically
match HybridSolver.solveTsp distances None None None with
| Ok solution ->
    printfn "Method used: %A" solution.Method  // Classical or Quantum
    printfn "Reasoning: %s" solution.Reasoning
    printfn "Solution: %A" solution.Result
| Error msg -> printfn "Error: %s" msg
```

### 🗺️ TSP (Traveling Salesman Problem)
Solve routing problems with named cities:

```fsharp
let cities = [("NYC", 40.7, -74.0); ("LA", 34.0, -118.2); ("Chicago", 41.9, -87.6)]

// Option 1: Direct solve (easiest)
let tour = TSP.solveDirectly cities None

// Option 2: Build problem first (for customization)
let problem = TSP.createProblem cities
let tour = TSP.solve problem (Some customConfig)
```

**Features:**
- Named cities with coordinates
- Automatic distance calculation
- Simulated annealing with 2-opt
- Configurable iterations and cooling

### 💼 Portfolio Optimization
Optimize investment portfolios with risk/return constraints:

```fsharp
let assets = [
    ("AAPL", 0.12, 0.18, 150.0)  // symbol, return, risk, price
    ("MSFT", 0.10, 0.15, 300.0)
    ("GOOGL", 0.15, 0.20, 2800.0)
]

let allocation = Portfolio.solveDirectly assets 10000.0 None

match allocation with
| Ok result ->
    printfn "Total Value: $%.2f" result.TotalValue
    printfn "Expected Return: %.2f%%" (result.ExpectedReturn * 100.0)
    printfn "Risk: %.2f" result.Risk
| Error msg -> printfn "Error: %s" msg
```

**Features:**
- Greedy return/risk ratio optimization
- Budget constraints
- Min/max holding limits
- Efficient allocation

### 🤖 Quantum Advisor
Get recommendations on when to use quantum vs classical:

```fsharp
match QuantumAdvisor.getRecommendation distances with
| Ok recommendation ->
    printfn "Recommendation: %A" recommendation.RecommendationType
    printfn "Problem size: %d" recommendation.ProblemSize
    printfn "Reasoning: %s" recommendation.Reasoning
| Error msg -> printfn "Error: %s" msg
```

### 🧪 Classical Solvers
Direct access to classical optimization algorithms:

```fsharp
// TSP Solver
let tspSolution = TspSolver.solveWithDistances distances TspSolver.defaultConfig

// Portfolio Solver  
let portfolio = PortfolioSolver.solveGreedyByRatio assets constraints PortfolioSolver.defaultConfig
```

### 🔬 Local Quantum Simulation
Test quantum algorithms offline without Azure credentials:

```fsharp
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Core.QuantumBackend
open FSharp.Azure.Quantum.Core.QaoaCircuit

// Create a QAOA circuit (example: 3-qubit MaxCut)
let quboMatrix = array2D [[0.0; 0.5; 0.5]; [0.5; 0.0; 0.5]; [0.5; 0.5; 0.0]]
let circuit = {
    NumQubits = 3
    InitialStateGates = [| H(0); H(1); H(2) |]
    Layers = [|
        {
            CostGates = [| RZZ(0, 1, 0.5); RZZ(1, 2, 0.5); RZZ(0, 2, 0.5) |]
            MixerGates = [| RX(0, 1.0); RX(1, 1.0); RX(2, 1.0) |]
            Gamma = 0.25
            Beta = 0.5
        }
    |]
    ProblemHamiltonian = ProblemHamiltonian.fromQubo quboMatrix
    MixerHamiltonian = MixerHamiltonian.create 3
}

// Execute on local simulator
match Local.simulate circuit 1000 with
| Ok result ->
    printfn "Backend: %s" result.Backend
    printfn "Time: %.2f ms" result.ExecutionTimeMs
    result.Counts
    |> Map.toList
    |> List.sortByDescending snd
    |> List.take 3
    |> List.iter (fun (bitstring, count) ->
        printfn "  %s: %d shots" bitstring count)
| Error msg ->
    eprintfn "Error: %s" msg
```

**Features:**
- State vector simulation (up to 10 qubits)
- QAOA circuit execution with mixer and cost Hamiltonians
- Measurement and shot sampling
- Zero external dependencies
- **Unified API**: Same `QaoaCircuit` type works with Azure backend (coming soon)

### 📊 Problem Analysis
Analyze problem complexity and characteristics:

```fsharp
match ProblemAnalysis.classifyProblem distances with
| Ok info ->
    printfn "Type: %A" info.ProblemType
    printfn "Size: %d" info.Size
    printfn "Complexity: %s" info.Complexity
| Error msg -> printfn "Error: %s" msg
```

### 💰 Cost Estimation
Estimate quantum execution costs before running:

```fsharp
let estimate = CostEstimation.estimateQuantumCost problemSize shots tier
printfn "Estimated cost: $%.2f %s" estimate.EstimatedCost estimate.Currency
```

## 📚 Documentation

- **[Getting Started Guide](docs/getting-started.md)** - Installation and first examples
- **[Local Simulation Guide](docs/local-simulation.md)** - Quantum simulation without Azure
- **[Backend Switching Guide](docs/backend-switching.md)** - Switch between local and Azure
- **[API Reference](docs/api-reference.md)** - Complete API documentation
- **[TSP Example](docs/examples/tsp-example.md)** - Detailed TSP walkthrough
- **[FAQ](docs/faq.md)** - Common questions and troubleshooting

## 🏗️ Architecture

**Current Status:** v0.1.0-alpha

### ✅ Completed Components

| Component | Status | Description |
|-----------|--------|-------------|
| **HybridSolver** (TKT-26) | ✅ Complete | Automatic quantum/classical routing |
| **TSP Builder** (TKT-24) | ✅ Complete | Domain API for TSP problems |
| **Portfolio Builder** (TKT-25) | ✅ Complete | Domain API for portfolio optimization |
| **TspSolver** (TKT-16) | ✅ Complete | Classical TSP with simulated annealing |
| **PortfolioSolver** (TKT-17) | ✅ Complete | Classical portfolio with greedy algorithm |
| **QuantumAdvisor** (TKT-19) | ✅ Complete | Quantum vs classical decision framework |
| **ProblemAnalysis** (TKT-18) | ✅ Complete | Problem classification and complexity |
| **CostEstimation** (TKT-27) | ✅ Complete | Cost calculation for quantum execution |
| **CircuitBuilder** (TKT-20) | ✅ Complete | Quantum circuit construction |
| **QuboEncoding** (TKT-21) | ✅ Complete | QUBO problem encoding |
| **QaoaCircuit** (TKT-22) | ✅ Complete | QAOA circuit generation |
| **QaoaOptimizer** (TKT-23) | ✅ Complete | QAOA parameter optimization |
| **Local Simulator** (TKT-61) | ✅ Complete | Offline quantum simulation (≤10 qubits) |

### 🚧 In Development

| Component | Status | Description |
|-----------|--------|-------------|
| **Quantum Backend** | 🚧 Planned | Azure Quantum service integration |
| **Advanced Constraints** | 🚧 Planned | Complex portfolio constraints |
| **More Domains** | 🚧 Planned | Scheduling, MaxCut, Knapsack |

## 🎯 When to Use Quantum

The library automatically recommends quantum vs classical based on:

### Use Classical When:
- ✅ Problem size < 50 variables
- ✅ Need immediate results (milliseconds)
- ✅ Developing/testing locally
- ✅ Cost is a concern

### Consider Quantum When:
- ⚡ Problem size > 100 variables
- ⚡ Problem has special structure (QUBO-compatible)
- ⚡ Can tolerate longer wait times (seconds)
- ⚡ Budget available (~$10-100 per run)

**Use HybridSolver to decide automatically!**

## 🔧 Development

### Prerequisites
- .NET 10.0 SDK
- F# 10.0

### Build
```bash
dotnet build
```

### Test
```bash
dotnet test
```

All 276 tests passing ✅ (including 73 local simulation tests)

### Run Examples
```bash
cd examples
dotnet run
```

## 📊 Performance

**Classical Solvers (Local Execution):**

| Problem | Size | Time | Quality |
|---------|------|------|---------|
| TSP | 10 cities | ~20ms | Optimal |
| TSP | 50 cities | ~500ms | Within 5% of optimal |
| TSP | 100 cities | ~2s | Within 10% of optimal |
| Portfolio | 20 assets | ~10ms | Optimal |
| Portfolio | 50 assets | ~50ms | Near-optimal |

## 🤝 Contributing

Contributions welcome! This is an alpha release and we're actively improving.

### Areas for Contribution
- Additional problem domains (Scheduling, MaxCut, etc.)
- Quantum backend integration
- Performance optimizations
- Documentation improvements
- Bug fixes

### Development Process
- Follow TDD methodology (see `docs-for-mulder/AI-DEVELOPMENT-GUIDE.md`)
- Write tests first (RED → GREEN → REFACTOR)
- Update documentation
- Submit PR to `dev` branch

## 📄 License

**Unlicense** - Public domain. Use freely for any purpose.

## 🙏 Acknowledgments

Built with:
- F# 10.0
- .NET 10.0
- Azure Quantum platform

Developed using AI-assisted TDD methodology.

## 📞 Support

- **Documentation**: [docs/](docs/)
- **Issues**: [GitHub Issues](https://github.com/thorium/FSharp.Azure.Quantum/issues)
- **Examples**: [docs/examples/](docs/examples/)

---

**Status**: Alpha (v0.1.0) - Classical solvers production-ready, quantum integration coming soon.

**Last Updated**: 2025-11-24
