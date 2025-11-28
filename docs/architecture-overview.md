# Architecture Overview

## Hybrid Quantum-Classical Design Philosophy

FSharp.Azure.Quantum is a **hybrid quantum-classical library** that intelligently combines:

- **Classical algorithms** for small problems (fast, cheap, CPU-based)
- **Quantum algorithms** for large problems (scalable, expensive, backend-based)
- **Automatic routing** based on problem analysis

This document explains the architecture, folder structure, and design decisions.

---

## Three-Layer Architecture

```
┌─────────────────────────────────────────────────────────────────────────┐
│  LAYER 1: HYBRID ORCHESTRATION (Solvers/Hybrid/)                        │
│  ├─ HybridSolver.fs        - Routes to classical OR quantum             │
│  ├─ QuantumAdvisor.fs      - Analyzes quantum advantage potential       │
│  └─ ProblemAnalysis.fs     - Classifies problem complexity              │
└─────────────────────────────────────────────────────────────────────────┘
                              │
             ┌────────────────┴────────────────┐
             │                                 │
┌────────────▼─────────────────┐  ┌───────────▼──────────────────────────┐
│ LAYER 2A: CLASSICAL SOLVERS  │  │ LAYER 2B: QUANTUM SOLVERS             │
│ (Solvers/Classical/)         │  │ (Solvers/Quantum/)                    │
│ ├─ TspSolver.fs              │  │ ├─ QuantumTspSolver.fs                │
│ └─ PortfolioSolver.fs        │  │ └─ QuantumChemistry.fs                │
│                              │  │                                       │
│ NO backend parameter         │  │ REQUIRES backend parameter            │
│ CPU execution only           │  │ Delegates to Layer 3                  │
└──────────────────────────────┘  └───────────────────────────────────────┘
                                                │
                               ┌────────────────┴────────────────┐
                               │                                 │
                  ┌────────────▼────────────┐      ┌────────────▼─────────────┐
                  │ LAYER 3A: BACKENDS      │      │ LAYER 3B: LOCAL SIM      │
                  │ (Backends/)             │      │ (LocalSimulator/)        │
                  │ ├─ IonQBackend.fs       │      │ ├─ StateVector.fs        │
                  │ ├─ RigettiBackend.fs    │      │ ├─ Gates.fs              │
                  │ └─ BackendAbstraction   │      │ ├─ Measurement.fs        │
                  │                         │      │ └─ QaoaSimulator.fs      │
                  │ Azure Quantum hardware  │      │                          │
                  │ Real QPUs, cloud sims   │      │ CPU-based, ≤10 qubits    │
                  └─────────────────────────┘      └──────────────────────────┘
```

---

## Folder Structure Explained

### `src/FSharp.Azure.Quantum/`

```
├── Core/                   - Foundation and Azure integration
│   ├── Types.fs           - Shared types across the library
│   ├── Authentication.fs  - Azure credential management
│   ├── Client.fs          - Azure Quantum workspace client
│   ├── QaoaCircuit.fs     - QAOA circuit construction
│   ├── QaoaOptimizer.fs   - QAOA parameter optimization
│   ├── CostEstimation.fs  - Quantum execution cost calculation
│   ├── CircuitValidator.fs - Pre-flight circuit validation
│   └── ...                - Job lifecycle, retry, batching, etc.
│
├── LocalSimulator/         - Offline quantum simulation (NO Azure)
│   ├── StateVector.fs     - Quantum state representation
│   ├── Gates.fs           - Gate operations (H, CNOT, RX, etc.)
│   ├── Measurement.fs     - Measurement and sampling
│   └── QaoaSimulator.fs   - QAOA-specific simulation
│
├── Backends/               - Quantum backend implementations
│   ├── BackendAbstraction.fs  - Unified backend interface
│   ├── IonQBackend.fs         - IonQ integration (simulator, QPU)
│   └── RigettiBackend.fs      - Rigetti integration (QVM, Aspen)
│
├── Solvers/
│   ├── Classical/         - CPU-based algorithms (NO backend param)
│   │   ├── TspSolver.fs          - Classical TSP (Nearest Neighbor, 2-opt)
│   │   └── PortfolioSolver.fs    - Classical portfolio (Greedy)
│   │
│   ├── Quantum/           - Quantum algorithms (REQUIRES backend)
│   │   ├── QuantumTspSolver.fs   - QAOA TSP solver
│   │   └── QuantumChemistry.fs   - VQE chemistry solver
│   │
│   └── Hybrid/            - Orchestration (routes classical + quantum)
│       ├── HybridSolver.fs       - Automatic routing logic
│       ├── QuantumAdvisor.fs     - Quantum advantage analysis
│       └── ProblemAnalysis.fs    - Problem classification
│
├── Algorithms/             - Pure quantum algorithms (NO problem-specific)
│   ├── GroverSearch.fs    - Grover's algorithm
│   ├── GroverOracle.fs    - Oracle construction
│   ├── GroverIteration.fs - Grover iteration operator
│   └── AmplitudeAmplification.fs
│
├── Builders/               - Problem encoding and circuit generation
│   ├── CircuitBuilder.fs         - Circuit construction DSL
│   ├── QuboEncoding.fs           - QUBO matrix encoding
│   ├── GraphOptimization.fs      - TSP → QUBO conversion
│   ├── SubsetSelection.fs        - Portfolio → QUBO conversion
│   ├── Scheduling.fs             - Job scheduling → QUBO
│   ├── OpenQasmExport.fs         - Export to OpenQASM 2.0
│   ├── OpenQasmImport.fs         - Import OpenQASM circuits
│   └── GateTranspiler.fs         - Gate set translation
│
├── ErrorMitigation/        - Quantum error mitigation techniques
│   ├── ErrorMitigationStrategy.fs
│   ├── ZeroNoiseExtrapolation.fs  (ZNE)
│   ├── ProbabilisticErrorCancellation.fs  (PEC)
│   └── ReadoutErrorMitigation.fs
│
└── Utilities/
    └── PerformanceBenchmarking.fs
```

---

## Key Architectural Decisions

### 1. Why "Classical" Folder in a Quantum Library?

**Problem:** Users see `Solvers/Classical/` and think "this doesn't belong here."

**Answer:** This is a **hybrid library** that uses:
- **Classical solvers** for small problems (cheap, fast)
- **Quantum solvers** for large problems (expensive, powerful)
- **Hybrid orchestration** to choose automatically

**Analogy:** Like a hybrid car with both gasoline engine (classical) and electric motor (quantum).

### 2. Algorithm vs Solver vs Backend

| Term | Definition | Location | Example |
|------|-----------|----------|---------|
| **Algorithm** | Mathematical approach | `Algorithms/` or within solver | Nearest Neighbor, QAOA, Grover |
| **Solver** | Problem-specific implementation | `Solvers/` | `TspSolver`, `QuantumTspSolver` |
| **Backend** | Execution environment | `Backends/`, `LocalSimulator/` | `IonQBackend`, `LocalSimulator` |

**Examples:**

```fsharp
// ALGORITHM: Nearest Neighbor (classical TSP heuristic)
// SOLVER: TspSolver (implements NN + 2-opt)
// BACKEND: None (executes on CPU)
let tour = TspSolver.solveWithDistances distances config

// ALGORITHM: QAOA (quantum optimization)
// SOLVER: QuantumTspSolver (TSP-specific QAOA)
// BACKEND: RigettiBackend (executes on Rigetti hardware)
let! result = QuantumTspSolver.solve rigettiBackend distances 1000
```

### 3. Backend Parameter Design

**Classical solvers** do NOT accept a backend parameter:

```fsharp
// ❌ COMPILE ERROR - TspSolver has no backend parameter
TspSolver.solveWithDistances distances config rigettiBackend

// ✅ CORRECT - Classical execution only
TspSolver.solveWithDistances distances config
```

**Quantum solvers** REQUIRE a backend parameter:

```fsharp
// ✅ Executes on Rigetti backend
QuantumTspSolver.solve rigettiBackend distances 1000

// ✅ Executes on IonQ backend
QuantumTspSolver.solve ionqBackend distances 1000

// ✅ Executes on local simulator
QuantumTspSolver.solve localBackend distances 1000
```

**Why separate solvers instead of optional backend?**

1. **Type safety**: Classical vs quantum is a fundamental architectural choice
2. **Clear intent**: Code explicitly shows whether quantum hardware is needed
3. **API simplicity**: Classical users don't see quantum-specific parameters
4. **Cost awareness**: Quantum execution has monetary cost, should be explicit

### 4. Folder Naming: "Classical" = Algorithm Type, Not Environment

**Misconception:** "`Solvers/Classical/` means CPU execution environment"

**Reality:** "`Solvers/Classical/` means classical **algorithms** (not quantum algorithms)"

```fsharp
// TspSolver uses CLASSICAL ALGORITHMS (Nearest Neighbor, 2-opt)
// Executes on CPU (because classical algorithms don't need quantum hardware)
TspSolver.solveWithDistances distances config

// QuantumTspSolver uses QUANTUM ALGORITHM (QAOA)
// Can execute on:
//   - Rigetti QPU (quantum hardware)
//   - IonQ simulator (cloud quantum simulator)
//   - LocalSimulator (CPU-based quantum simulation)
QuantumTspSolver.solve backend distances 1000
```

**The backend parameter** makes quantum solvers **environment-agnostic**:
- Same `QuantumTspSolver.solve` function works with ANY backend
- Backend abstraction handles IonQ vs Rigetti vs Local differences
- Quantum **algorithm** stays the same, only **execution** changes

---

## Execution Flow Examples

### Example 1: Classical TSP (Small Problem)

```fsharp
open FSharp.Azure.Quantum.Classical

let cities = [("NYC", 40.7, -74.0); ("LA", 34.0, -118.2)]

// User calls classical solver directly
let result = TspSolver.solveWithDistances distances TspSolver.defaultConfig

// Execution path:
// 1. TspSolver.solveWithDistances (Solvers/Classical/TspSolver.fs)
// 2. Nearest Neighbor initialization (CPU)
// 3. 2-opt local search (CPU)
// 4. Return tour
//
// NO quantum backend involved
// NO Azure Quantum API calls
// Execution time: ~20ms
// Cost: $0
```

### Example 2: Quantum TSP (Large Problem)

```fsharp
open FSharp.Azure.Quantum.Quantum
open FSharp.Azure.Quantum.Core

// User creates backend
let rigettiBackend = RigettiBackend.create httpClient workspaceUrl "rigetti.qpu.aspen-m-3"

// User calls quantum solver with backend parameter
let! result = QuantumTspSolver.solve rigettiBackend distances 1000

// Execution path:
// 1. QuantumTspSolver.solve (Solvers/Quantum/QuantumTspSolver.fs)
// 2. Convert TSP → QUBO matrix (Builders/GraphOptimization.fs)
// 3. Build QAOA circuit (Core/QaoaCircuit.fs)
// 4. Validate circuit (Core/CircuitValidator.fs)
// 5. Submit to Rigetti backend (Backends/RigettiBackend.fs)
// 6. Azure Quantum job submission (Core/Client.fs)
// 7. Poll for results (Core/JobLifecycle.fs)
// 8. Decode measurements → TSP tour
// 9. Return solution
//
// Quantum backend: Rigetti Aspen-M-3 QPU
// Execution time: ~30 seconds (includes queue wait)
// Cost: ~$50-100
```

### Example 3: Hybrid Automatic Routing

```fsharp
open FSharp.Azure.Quantum.Classical

// User calls HybridSolver (doesn't specify method)
match HybridSolver.solveTsp distances None None None with
| Ok solution ->
    match solution.Method with
    | Classical -> 
        // Routed to TspSolver (small problem)
        printfn "Used classical: %s" solution.Reasoning
    | Quantum ->
        // Routed to QuantumTspSolver with backend (large problem)
        printfn "Used quantum: %s" solution.Reasoning
| Error msg -> printfn "Error: %s" msg

// Execution path:
// 1. HybridSolver.solveTsp (Solvers/Hybrid/HybridSolver.fs)
// 2. ProblemAnalysis.classifyProblem (analyzes size, structure)
// 3. QuantumAdvisor.getRecommendation (estimates quantum advantage)
// 4. Decision:
//    - If small problem → call TspSolver (classical)
//    - If large problem → call QuantumTspSolver with backend (quantum)
// 5. Return unified result
```

---

## Design Patterns

### 1. Builder Pattern (Problem Construction)

Problem-specific builders in `Builders/`:

```fsharp
// TSP Builder API (high-level)
let problem = TSP.createProblem cities
let result = TSP.solve problem (Some config)

// Internally uses:
// - GraphOptimization.toQubo (Builders/GraphOptimization.fs)
// - QaoaCircuit.build (Core/QaoaCircuit.fs)
```

### 2. Backend Abstraction Pattern

Unified interface for all quantum backends:

```fsharp
// BackendAbstraction.fs defines common interface
type IQuantumBackend =
    abstract member SubmitJob : circuit -> Async<JobId>
    abstract member GetResults : JobId -> Async<Histogram>

// IonQBackend.fs implements interface
module IonQBackend =
    let submitAndWaitForResults httpClient workspaceUrl circuit shots target = ...

// RigettiBackend.fs implements interface
module RigettiBackend =
    let submitAndWaitForResults httpClient workspaceUrl circuit shots target = ...

// QuantumTspSolver uses ANY backend via abstraction
let solve (backend: IQuantumBackend) distances shots = ...
```

### 3. Validation Pattern (Pre-Flight Checks)

Validate before expensive operations:

```fsharp
// CircuitValidator.fs - extensible constraint system
let constraints = BackendConstraints.ionqSimulator()
match validateCircuit constraints circuit with
| Ok () -> 
    // Proceed with submission
| Error errors -> 
    // Fix circuit before expensive API call
```

### 4. Layered Abstraction

Each layer only depends on layers below:

```
Hybrid (Layer 1)
  ↓ depends on
Classical Solvers (Layer 2A) + Quantum Solvers (Layer 2B)
  ↓ depends on
Backends (Layer 3A) + LocalSimulator (Layer 3B)
```

**Example:**
- `HybridSolver.fs` calls `TspSolver.fs` OR `QuantumTspSolver.fs`
- `QuantumTspSolver.fs` calls `IonQBackend.fs` OR `RigettiBackend.fs`
- `IonQBackend.fs` calls Azure Quantum REST API

**NO circular dependencies** - clean unidirectional flow.

---

## Common Misconceptions

### ❌ "I should be able to pass a backend to TspSolver"

**Why it's wrong:** `TspSolver` uses classical algorithms (Nearest Neighbor, 2-opt) that execute on CPU. There's no quantum circuit to submit to a backend.

**Correct approach:** Use `QuantumTspSolver` if you want quantum execution.

### ❌ "Classical folder should be named 'CPU' or 'Local'"

**Why it's wrong:** The folder name refers to the **algorithm type** (classical vs quantum), not the execution environment.

**Proof:** `QuantumTspSolver` can execute on:
- `LocalSimulator` (CPU-based simulation)
- `IonQBackend` (cloud quantum hardware)
- Both are quantum **algorithms**, different execution **environments**

### ❌ "Why not make backend an optional parameter?"

**Why it's wrong:** Classical and quantum are fundamentally different architectural choices:

```fsharp
// ❌ BAD DESIGN - optional backend is confusing
let solve distances config (backend: IQuantumBackend option) =
    match backend with
    | None -> 
        // Classical execution (Nearest Neighbor)
    | Some qBackend -> 
        // Quantum execution (QAOA)
        // Completely different algorithm!

// ✅ GOOD DESIGN - separate functions make intent clear
let solveTspClassical distances config = ...     // Classical algorithm
let solveTspQuantum backend distances shots = ... // Quantum algorithm
```

**Benefits of separation:**
1. **Type safety**: Compiler enforces backend for quantum
2. **Clear cost**: Quantum execution is visibly different
3. **Simpler APIs**: Classical users don't see quantum params
4. **Maintainability**: Classical and quantum code evolve independently

---

## Future Extensions

### Adding New Classical Solvers

1. Create `Solvers/Classical/NewSolver.fs`
2. Implement CPU-based algorithm (NO backend parameter)
3. Add to HybridSolver routing logic

### Adding New Quantum Solvers

1. Create `Solvers/Quantum/QuantumNewSolver.fs`
2. Accept `backend` parameter (type: `IQuantumBackend`)
3. Build QUBO/circuit in `Builders/`
4. Add to HybridSolver routing logic

### Adding New Backends

1. Create `Backends/NewBackend.fs`
2. Implement `IQuantumBackend` interface
3. Handle provider-specific circuit format
4. Integrate with Azure Quantum or other provider

---

## Summary

| Component | Location | Purpose | Backend Param? |
|-----------|----------|---------|----------------|
| **TspSolver** | `Solvers/Classical/` | Classical TSP (Nearest Neighbor, 2-opt) | ❌ No |
| **PortfolioSolver** | `Solvers/Classical/` | Classical portfolio (Greedy) | ❌ No |
| **QuantumTspSolver** | `Solvers/Quantum/` | Quantum TSP (QAOA) | ✅ Required |
| **QuantumChemistry** | `Solvers/Quantum/` | Quantum chemistry (VQE) | ✅ Required |
| **HybridSolver** | `Solvers/Hybrid/` | Orchestrates classical OR quantum | ⚡ Routes to appropriate solver |
| **IonQBackend** | `Backends/` | IonQ integration | N/A (execution layer) |
| **RigettiBackend** | `Backends/` | Rigetti integration | N/A (execution layer) |
| **LocalSimulator** | `LocalSimulator/` | CPU-based quantum simulation | N/A (execution layer) |

**Key Takeaway:** 
- **Classical solvers** = classical **algorithms** (CPU execution, no backend)
- **Quantum solvers** = quantum **algorithms** (backend execution, requires backend)
- **Hybrid solver** = **orchestration** (routes to appropriate solver automatically)
