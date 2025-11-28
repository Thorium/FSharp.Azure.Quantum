# FSharp.Azure.Quantum

**Hybrid Quantum-Classical F# Library** - Intelligently routes optimization problems between classical algorithms (fast, cheap) and quantum backends (scalable, powerful) based on problem size and structure.

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

**Production-Ready Azure Quantum Features:**
- ✅ IonQ simulator and QPU (`ionq.simulator`, `ionq.qpu.aria-1`)
- ✅ Rigetti QVM and Aspen QPU (`rigetti.sim.qvm`, `rigetti.qpu.aspen-m-3`)
- ✅ Azure authentication via Azure.Identity (CLI, Managed Identity)
- ✅ Pre-flight circuit validation (catch errors before submission)
- ✅ Cost limit enforcement and error handling
- ✅ Multi-provider QAOA support (OpenQASM 2.0)

**Coming in v1.0:**
- 🎯 QUBO-to-circuit conversion for TSP/Portfolio problems
- 🎯 Advanced result comparison and quantum advantage validation
- 🎯 Support for IBM Quantum, Amazon Braket, Google Cirq

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

**QAOA Multi-Provider Support:**

QAOA circuits use **OpenQASM 2.0** format, making them compatible with all major quantum providers:

```fsharp
open FSharp.Azure.Quantum.Core.QaoaCircuit

// Build QAOA circuit (provider-agnostic)
let quboMatrix = array2D [[1.0; -2.0]; [-2.0; 1.0]]
let problemHam = ProblemHamiltonian.fromQubo quboMatrix
let mixerHam = MixerHamiltonian.create 2
let circuit = QaoaCircuit.build problemHam mixerHam [|(0.5, 0.3)|]

// Export to OpenQASM 2.0 (universal format)
let qasm = QaoaCircuit.toOpenQasm circuit
printfn "%s" qasm
// Output:
// OPENQASM 2.0;
// include "qelib1.inc";
// qreg q[2];
// // Initial state preparation
// h q[0];
// h q[1];
// ...
```

**Provider Compatibility:**
- ✅ **IonQ** - Native OpenQASM or JSON gate format
- ✅ **Rigetti** - Translate OpenQASM → Quil (assembly language)
- ✅ **IBM Qiskit** - Native OpenQASM 2.0 support
- ✅ **Amazon Braket** - OpenQASM support
- ✅ **Google Cirq** - OpenQASM import capability
- ✅ **Local Simulator** - Direct QAOA execution (no translation needed)

**Why QAOA is Provider-Agnostic:**
- Uses **standard gate set** (H, RX, RY, RZ, RZZ, CNOT)
- **OpenQASM 2.0** is the industry standard for circuit interchange
- Algorithm logic is **separate from backend submission** code

### ☁️ Azure Quantum Integration (v0.5.0-beta)
Execute quantum circuits on Azure Quantum backends (IonQ, Rigetti):

```fsharp
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Core.Authentication
open FSharp.Azure.Quantum.Core.IonQBackend

// Step 1: Create authenticated HTTP client
let credential = CredentialProviders.createDefaultCredential()  // Uses Azure CLI, Managed Identity, etc.
let httpClient = Authentication.createAuthenticatedClient credential

// Step 2: Build workspace URL
let subscriptionId = "your-subscription-id"
let resourceGroup = "your-resource-group"
let workspaceName = "your-workspace-name"
let location = "eastus"
let workspaceUrl = 
    $"https://management.azure.com/subscriptions/{subscriptionId}/resourceGroups/{resourceGroup}/providers/Microsoft.Quantum/Workspaces/{workspaceName}"

// Step 3: Create IonQ circuit (Bell state example)
let circuit: IonQCircuit = {
    Qubits = 2
    Circuit = [
        SingleQubit("h", 0)      // Hadamard on qubit 0
        TwoQubit("cnot", 0, 1)   // CNOT with control=0, target=1
        Measure([|0; 1|])        // Measure both qubits
    ]
}

// Step 4: Submit and execute
async {
    let! result = IonQBackend.submitAndWaitForResultsAsync httpClient workspaceUrl circuit 100 "ionq.simulator"
    
    match result with
    | Ok histogram ->
        printfn "✅ Job completed!"
        histogram 
        |> Map.iter (fun bitstring count -> 
            printfn "  %s: %d shots" bitstring count)
    | Error err ->
        eprintfn "❌ Error: %A" err
} |> Async.RunSynchronously
```

**Supported Backends:**
- `ionq.simulator` - IonQ cloud simulator
- `ionq.qpu.aria-1` - IonQ Aria-1 QPU (requires credits)
- `rigetti.sim.qvm` - Rigetti QVM simulator
- `rigetti.qpu.aspen-m-3` - Rigetti Aspen-M-3 QPU (requires credits)

**Authentication Methods:**
- `DefaultAzureCredential()` - Tries Azure CLI, Managed Identity, Environment Variables
- `AzureCliCredential()` - Uses `az login` credentials  
- `ManagedIdentityCredential()` - For Azure VMs/App Services

**Features:**
- Automatic token acquisition and refresh
- Job submission, polling, and result retrieval
- Error handling with retry logic
- Cost tracking and estimation

### 🔍 Circuit Validation (Pre-Flight Checks)

Validate circuits against backend constraints **before submission** to catch errors early and avoid costly failed API calls:

```fsharp
open FSharp.Azure.Quantum.Core.CircuitValidator

// Example: Validate circuit for IonQ simulator
let circuit = {
    NumQubits = 5
    GateCount = 50
    UsedGates = Set.ofList ["H"; "CNOT"; "RX"]
    TwoQubitGates = [(0, 1); (1, 2)]
}

let constraints = BackendConstraints.ionqSimulator()
match validateCircuit constraints circuit with
| Ok () -> 
    printfn "✅ Circuit valid for IonQ simulator"
| Error errors ->
    printfn "❌ Validation failed:"
    errors |> List.iter (fun err -> 
        printfn "  - %s" (formatValidationError err))
```

**Built-in Backend Constraints:**

```fsharp
// Local simulator (1-10 qubits, all gates, no depth limit)
let local = BackendConstraints.localSimulator()

// IonQ simulator (29 qubits, all-to-all connectivity, 100 gate limit)
let ionqSim = BackendConstraints.ionqSimulator()

// IonQ hardware (11 qubits, all-to-all connectivity, 100 gate limit)
let ionqHw = BackendConstraints.ionqHardware()

// Rigetti Aspen-M-3 (79 qubits, limited connectivity, 50 gate limit)
let rigetti = BackendConstraints.rigettiAspenM3()
```

**Auto-Detection from Target String:**

```fsharp
// Automatically detect constraints from Azure Quantum target
match KnownTargets.getConstraints "ionq.simulator" with
| Some constraints -> 
    // Validate circuit...
    validateCircuit constraints myCircuit
| None -> 
    printfn "Unknown target - provide custom constraints"
```

**Integrated Validation (IonQ):**

```fsharp
// IonQ backend validates automatically before submission
async {
    let! result = IonQBackend.submitAndWaitForResultsWithValidationAsync
        httpClient
        workspaceUrl
        circuit
        1000  // shots
        "ionq.simulator"
        None  // Auto-detect constraints from target string
    
    match result with
    | Ok histogram -> printfn "Success!"
    | Error (InvalidCircuit errors) -> 
        printfn "Circuit validation failed before submission:"
        errors |> List.iter (printfn "  %s")
    | Error otherError -> 
        printfn "Execution error: %A" otherError
} |> Async.RunSynchronously
```

**Custom Backend Constraints:**

```fsharp
// Define constraints for a new quantum provider
let ibmConstraints = BackendConstraints.create
    "IBM Quantum Eagle"      // Name
    127                      // Max qubits
    ["H"; "X"; "CX"; "RZ"]  // Supported gates
    (Some 1000)              // Max circuit depth
    false                    // Limited connectivity
    [(0,1); (1,2); (2,3)]   // Connected qubit pairs

// Use custom constraints
match validateCircuit ibmConstraints myCircuit with
| Ok () -> printfn "Valid for IBM Quantum!"
| Error errors -> printfn "Validation errors: %A" errors
```

**Validation Checks:**
- ✅ **Qubit count** - Does circuit exceed backend qubit limit?
- ✅ **Gate set** - Are all gates supported by the backend?
- ✅ **Circuit depth** - Does circuit exceed recommended depth limit?
- ✅ **Connectivity** - Do two-qubit gates respect topology constraints?

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

- **[Architecture Overview](docs/architecture-overview.md)** - Hybrid quantum-classical design explained
- **[Getting Started Guide](docs/getting-started.md)** - Installation and first examples
- **[Local Simulation Guide](docs/local-simulation.md)** - Quantum simulation without Azure
- **[Backend Switching Guide](docs/backend-switching.md)** - Switch between local and Azure
- **[API Reference](docs/api-reference.md)** - Complete API documentation
- **[TSP Example](docs/examples/tsp-example.md)** - Detailed TSP walkthrough
- **[FAQ](docs/faq.md)** - Common questions and troubleshooting

## 🏗️ Architecture

**Current Status:** v0.5.0-beta - Azure Quantum Integration Ready

### Hybrid Quantum-Classical Design

This library uses a **three-layer architecture** that combines classical and quantum approaches:

```
┌─────────────────────────────────────────────────────────┐
│  LAYER 1: HYBRID ORCHESTRATION (Decision Layer)         │
│  - HybridSolver: Routes to classical or quantum         │
│  - QuantumAdvisor: Analyzes quantum advantage potential │
│  - ProblemAnalysis: Classifies problem complexity       │
└─────────────────────────────────────────────────────────┘
                           │
          ┌────────────────┴────────────────┐
          │                                 │
┌─────────▼──────────────┐     ┌───────────▼──────────────┐
│ LAYER 2A: CLASSICAL    │     │ LAYER 2B: QUANTUM         │
│ - TspSolver            │     │ - QuantumTspSolver        │
│ - PortfolioSolver      │     │ - QuantumChemistry        │
│ (NO backend parameter) │     │ (REQUIRES backend param)  │
└────────────────────────┘     └───────────────────────────┘
                                            │
                           ┌────────────────┴────────────────┐
                           │                                 │
                ┌──────────▼─────────┐          ┌───────────▼──────────┐
                │ LAYER 3A: BACKENDS │          │ LAYER 3B: SIMULATION │
                │ - IonQBackend      │          │ - LocalSimulator     │
                │ - RigettiBackend   │          │ (≤10 qubits, fast)   │
                │ (Azure Quantum)    │          │                      │
                └────────────────────┘          └──────────────────────┘
```

### Why Classical Solvers in a Quantum Library?

**Q: Can I execute `TspSolver` on a quantum backend?**

**A: NO** - `TspSolver` uses **classical algorithms** (Nearest Neighbor, 2-opt) and has **NO backend parameter**:

```fsharp
// ❌ COMPILE ERROR - TspSolver doesn't accept a backend
TspSolver.solveWithDistances distances config rigettiBackend

// ✅ CORRECT - Classical execution only
let result = TspSolver.solveWithDistances distances TspSolver.defaultConfig
```

**Q: How do I use quantum backends?**

**A:** Use **`QuantumTspSolver`** which **requires a backend parameter**:

```fsharp
// ✅ Execute on Rigetti backend
let! result = QuantumTspSolver.solve rigettiBackend distances 1000

// ✅ Execute on IonQ backend  
let! result = QuantumTspSolver.solve ionqBackend distances 1000

// ✅ Execute on local simulator
let! result = QuantumTspSolver.solve localBackend distances 1000
```

### Algorithm vs Solver vs Backend

| Term | Meaning | Example |
|------|---------|---------|
| **Algorithm** | Mathematical approach (classical or quantum) | Nearest Neighbor, QAOA, Grover |
| **Solver** | Implementation of algorithm(s) | `TspSolver` (classical algos), `QuantumTspSolver` (QAOA) |
| **Backend** | Execution environment | `IonQBackend`, `RigettiBackend`, `LocalSimulator` |

**Key Point:** 
- **Classical solvers** execute algorithms on CPU (no backend needed)
- **Quantum solvers** execute algorithms via backends (backend parameter required)
- **Hybrid solver** automatically chooses based on problem size

### Automatic Routing with HybridSolver

```fsharp
// HybridSolver analyzes the problem and routes automatically
match HybridSolver.solveTsp distances None None None with
| Ok solution ->
    match solution.Method with
    | Classical -> 
        printfn "Used classical solver (fast, cheap)"
        printfn "Reason: %s" solution.Reasoning  // e.g., "Problem size < 50"
    | Quantum ->
        printfn "Used quantum backend (scalable, expensive)"
        printfn "Reason: %s" solution.Reasoning  // e.g., "Large problem, quantum advantage likely"
| Error msg -> printfn "Error: %s" msg
```

**Routing Logic:**
- **Small problems (< 50 variables)**: Classical solvers (milliseconds, $0)
- **Large problems (> 100 variables)**: Quantum backends (seconds, ~$10-100)
- **Medium problems (50-100)**: Configurable threshold or cost-based decision

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
| **QaoaCircuit** (TKT-22) | ✅ Complete | QAOA circuit generation (OpenQASM 2.0) |
| **QaoaOptimizer** (TKT-23) | ✅ Complete | QAOA parameter optimization |
| **Local Simulator** (TKT-61) | ✅ Complete | Offline quantum simulation (≤10 qubits) |
| **CircuitValidator** | ✅ Complete | Pre-flight validation with extensible constraints |
| **IonQ Backend** (TKT-39) | ✅ Complete | IonQ simulator & QPU integration |
| **Rigetti Backend** (TKT-40) | ✅ Complete | Rigetti QVM & Aspen QPU integration |
| **Azure Authentication** | ✅ Complete | Azure.Identity integration (CLI, Managed Identity) |

### 🧱 Problem Domain Builders

Generic frameworks for encoding optimization problems into quantum-solvable formats (QUBO):

| Builder | Problem Domains | Description |
|---------|----------------|-------------|
| **GraphOptimization** | TSP, Graph Coloring, MaxCut | Graph-based optimization with nodes, edges, and constraints |
| **SubsetSelection** | Knapsack, Portfolio, Set Cover | Select optimal subset with multi-dimensional weights and constraints |
| **Scheduling** | Task Scheduling, Job Shop, Resource Allocation | Schedule tasks with dependencies, resources, and time constraints |
| **ConstraintSatisfaction** | N-Queens, Sudoku, Map Coloring | Solve CSP problems with variables and constraints |
| **QuboEncoding** | Generic QUBO | Low-level QUBO matrix construction with variable encoding strategies |
| **CircuitBuilder** | Generic Circuits | Type-safe quantum circuit construction (gates, measurements) |
| **OpenQasmExport** | Circuit Export | Export circuits to OpenQASM 2.0 (IBM Qiskit compatible) |
| **GateTranspiler** | Gate Decomposition | Decompose high-level gates to backend-native gate sets |

**Key Features:**
- **Fluent API**: Chain builder methods for problem specification
- **QUBO Encoding**: Automatic conversion to quantum-solvable format
- **Multi-solver Support**: Use classical (DP, greedy) or quantum (QAOA) solvers
- **Extensible**: Add custom constraints and objectives

**Example - Graph Coloring with GraphOptimization:**
```fsharp
let problem =
    GraphOptimizationBuilder()
        .Nodes([node "A" 1; node "B" 2; node "C" 3])
        .Edges([edge "A" "B" 1.0; edge "B" "C" 1.0])
        .AddConstraint(NoAdjacentEqual)
        .Objective(MinimizeColors)
        .NumColors(3)
        .Build()

let qubo = GraphOptimization.toQubo problem
// Use with quantum solver or classical solver
```

**Example - Knapsack with SubsetSelection:**
```fsharp
let problem =
    SubsetSelectionBuilder()
        .Items([
            item "laptop" 15.0 |> withWeight "weight" 3.0
            item "phone" 10.0 |> withWeight "weight" 1.0
        ])
        .AddConstraint(TotalWeightLimit("weight", 5.0))
        .Objective(MaximizeValue)
        .Build()

let qubo = SubsetSelection.toQubo problem
```

### 🚧 In Development

| Component | Status | Description |
|-----------|--------|-------------|
| **QUBO-to-Circuit** | 🚧 Planned | Automatic TSP/Portfolio → QAOA circuit conversion |
| **Advanced Constraints** | 🚧 Planned | Complex portfolio constraints |
| **More Domains** | 🚧 Planned | Scheduling, MaxCut, Knapsack |
| **IBM/Google/Amazon** | 🚧 Future | Additional quantum provider support |

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

All 548 tests passing ✅ (including local simulation, Azure Quantum backends, and validation tests)

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
