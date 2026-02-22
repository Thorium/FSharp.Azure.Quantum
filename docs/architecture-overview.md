# Architecture Overview

## Design Philosophy

FSharp.Azure.Quantum is a **quantum-first optimization library** with intelligent classical fallback:

- **Quantum solvers (Primary)** - QAOA/VQE algorithms for optimization problems via quantum backends (LocalBackend or Azure Quantum)
- **Classical solvers (Fallback)** - Fast CPU algorithms for small problems (< 20 variables) where quantum advantage isn't yet beneficial
- **Hybrid orchestration** - HybridSolver automatically routes based on problem size, complexity, and available resources

**Philosophy**: Quantum algorithms are the primary approach. Classical solvers serve as an optimization for very small problems where quantum overhead isn't justified.

## Four-Layer Quantum-First Architecture

```
LAYER 1: User-Facing API
  ├─ Business Builders (SocialNetworkAnalyzer, ConstraintScheduler, CoverageOptimizer, etc.) → Domain-specific computation expressions
  ├─ High-Level Builders (GraphColoring, MaxCut, TSP, Portfolio, etc.) → Quantum computation expressions
  └─ HybridSolver API (Optional) → Automatic quantum/classical routing for optimization

LAYER 2: Problem Solvers
  ├─ Quantum Solvers (Primary)
  │   ├─ QAOA-based (GraphColoring, MaxCut, Knapsack, TSP, Portfolio, NetworkFlow)
  │   ├─ VQE (Quantum Chemistry)
  │   ├─ QFT-based (Arithmetic, Shor's Factorization, Phase Estimation)
  │   └─ Educational (Grover, Amplitude Amplification, QFT)
  │
  └─ Classical Solvers (Fallback, only via HybridSolver)
      ├─ TspSolver (Nearest Neighbor, 2-opt)
      └─ PortfolioSolver (Greedy Ratio)

LAYER 3: Problem Decomposition
  └─ ProblemDecomposition (Core/ProblemDecomposition.fs)
      ├─ Backend-aware problem partitioning (splits large problems for target backend constraints)
      ├─ QUBO/Ising sub-problem generation
      └─ Result aggregation across decomposed sub-problems

LAYER 4: Execution Backends
  ├─ LocalBackend (CPU simulation, ≤20 qubits, default)
  ├─ Cloud Backends (via CloudBackendFactory)
  │   ├─ RigettiCloudBackend (superconducting qubits)
  │   ├─ IonQCloudBackend (trapped ions)
  │   ├─ QuantinuumCloudBackend (trapped ions)
  │   └─ AtomComputingCloudBackend (neutral atoms)
  └─ All backends implement IQuantumBackend (sync + async)
```

**Key Architectural Principle**: High-level builders go directly to quantum solvers. Classical solvers are only accessible through HybridSolver for performance optimization of small problems.

**Architecture Flow**: Business Builders → Solvers → ProblemDecomposition → QaoaExecutionHelpers → QaoaCircuit → Backend

## Key Concepts

### Quantum-First Approach

**Business Builders** (`SocialNetworkAnalyzer`, `ConstraintScheduler`, `CoverageOptimizer`, `ResourcePairing`, `PackingOptimizer`):
- Domain-specific computation expression builders in the `Business/` folder
- Encode real-world business problems into the quantum optimization pipeline
- Internally delegate to high-level builders and solvers
- Flow: Business Builders → Solvers → ProblemDecomposition → Backend

**High-Level Builders** (`GraphColoring`, `MaxCut`, `TSP`, `Portfolio`, etc.):
- Use quantum algorithms directly (QAOA, VQE, QFT)
- **Require backend parameter** - submit circuits to quantum hardware/simulator
- Recommended for all problem sizes
- Example: `GraphColoring.solve problem 4 None` → LocalBackend (default)

**Classical Solvers** (`TspSolver`, `PortfolioSolver` - internal only):
- Use CPU algorithms (Nearest Neighbor, Greedy, 2-opt)
- **Only accessible via HybridSolver** - not exposed directly
- Automatically used for very small problems (< 20 variables)
- Example: `HybridSolver.solveTsp distances None None None` → Routes automatically

### Why Both?

**Direct Quantum (Recommended)**:
- Consistent API across all problem sizes
- Leverages quantum algorithms (QAOA/VQE/QFT)
- LocalBackend provides free, fast simulation (≤20 qubits)
- Future-proof as quantum hardware improves

**HybridSolver (Optional Optimization)**:
- Automatically optimizes very small problems using classical algorithms
- Saves quantum circuit overhead for problems too small to benefit
- Transparent routing with reasoning provided
- Use when performance optimization matters for variable problem sizes

### Builder Routing Architecture

High-Level Builders (`GraphColoring`, `MaxCut`, `TSP`, `Portfolio`) provide a business-friendly quantum API that encodes problems as QUBO/Ising models and solves them using QAOA or VQE. Business Builders (`SocialNetworkAnalyzer`, `ConstraintScheduler`, etc.) provide domain-specific APIs that delegate to high-level builders.

**Direct Quantum Routing (Default):**
```
User → GraphColoring.solve(problem, colors, backend)
         ↓
       Encode as QUBO
         ↓
       Build QAOA Circuit
         ↓
       ProblemDecomposition (partition for backend constraints)
         ↓
       Execute on Backend (LocalBackend default)
         ↓
       Decode Bitstring → Color Assignments
         ↓
       Return Result<Solution, string>
```

**HybridSolver Routing (Optional):**
```
User → HybridSolver.solveGraphColoring(problem, colors, budget, timeout, forceMethod)
         ↓
       Analyze Problem (size, complexity)
         ↓
       Decision: Size < 20? → Classical (fast)
                 Size ≥ 20? → Quantum (scalable)
         ↓
       Execute chosen method
         ↓
       Return Result<Solution, string> + Reasoning
```

**Benefits:**
- ✅ **Direct Builders**: Simple quantum API, consistent across problem sizes, future-proof
- ✅ **HybridSolver**: Automatic optimization for variable-sized problems, transparent reasoning
- ✅ **Type-safe**: F# Result types for error handling
- ✅ **Backend abstraction**: LocalBackend (simulation) or cloud backends (IonQ/Rigetti)

**Example:**
```fsharp
open FSharp.Azure.Quantum.GraphColoring

// Direct Quantum Approach (Recommended) - consistent API
let problem = graphColoring {
    node "R1" ["R2"; "R3"]
    node "R2" ["R1"; "R4"]
}
match GraphColoring.solve problem 4 None with  // None = LocalBackend
| Ok solution -> printfn "Colors Used: %d" solution.ColorsUsed
| Error err -> printfn "Error: %s" err.Message

// HybridSolver (Optional) - automatic classical fallback for small problems
match HybridSolver.solveGraphColoring problem 4 None None None with
| Ok solution -> 
    printfn "Method: %A" solution.Method  // Shows Classical or Quantum
    printfn "Reasoning: %s" solution.Reasoning
    printfn "Colors Used: %d" solution.Result.ColorsUsed
| Error err -> printfn "Error: %s" err.Message
```

## Folder Structure

```
src/FSharp.Azure.Quantum/
├── Core/              - Foundation (types, auth, QAOA, VQE, circuit operations)
│   └── ProblemDecomposition.fs - Backend-aware problem decomposition and partitioning
├── LocalSimulator/    - CPU-based quantum simulation (default backend)
├── Backends/          - Cloud backend integration (Rigetti, IonQ, Quantinuum, AtomComputing)
│   └── CloudBackends.fs - Cloud backends + CloudBackendFactory
├── MachineLearning/   - Quantum ML (VQC, QuantumKernel, QuantumKernelSVM)
├── Solvers/
│   ├── Classical/     - CPU algorithms (internal, only via HybridSolver)
│   ├── Quantum/       - Quantum algorithms (QAOA, VQE, QFT-based - primary solvers)
│   └── Hybrid/        - Optional routing logic (HybridSolver)
├── Algorithms/        - Grover, Amplitude Amplification, QFT (educational & building blocks)
├── Builders/          - High-level APIs (GraphColoring, MaxCut, TSP, etc.)
├── Business/          - Domain-specific computation expression builders
│   ├── SocialNetworkAnalyzer.fs  - Community detection and influence optimization
│   ├── ConstraintScheduler.fs    - Constraint-based scheduling optimization
│   ├── CoverageOptimizer.fs      - Set cover and facility location problems
│   ├── ResourcePairing.fs        - Bipartite matching and resource assignment
│   └── PackingOptimizer.fs       - Bin packing and knapsack variants
├── ErrorMitigation/   - ZNE, PEC, REM (reduce quantum noise)
└── Utilities/         - Performance benchmarking, circuit optimization
```

## Common Questions

**Q: Should I use the high-level builders or HybridSolver?**

A: **Use high-level builders directly** (`GraphColoring.solve`, `MaxCut.solve`, etc.) for most cases. They provide:
- Consistent quantum API across all problem sizes
- LocalBackend simulation (free, fast, ≤20 qubits)
- Future-proof as quantum hardware improves

Use HybridSolver only if you need automatic classical fallback for very small problems (< 20 variables) where quantum circuit overhead isn't justified.

**Q: Can I access classical solvers directly?**

A: No. Classical solvers (`TspSolver`, `PortfolioSolver`) are internal implementation details, only accessible via `HybridSolver`. The primary API is quantum-first.

**Q: What's the difference between Algorithm, Solver, Builder, and Backend?**

- **Algorithm** - Mathematical approach (QAOA, Grover, QFT, Shor)
- **Solver** - Problem-specific implementation (uses algorithms internally)
- **ProblemDecomposition** - Backend-aware partitioning layer that splits large problems into sub-problems matching backend constraints
- **Builder** - User-facing API with computation expressions (e.g., `graphColoring { ... }`)
- **Business Builder** - Domain-specific builder for real-world problems (e.g., `SocialNetworkAnalyzer`, `ConstraintScheduler`)
- **Backend** - Execution environment (LocalBackend, IonQBackend, RigettiBackend)

## Design Patterns

**Computation Expression Pattern**: Fluent, type-safe problem construction
```fsharp
let problem = graphColoring {
    node "R1" ["R2"; "R3"]
    node "R2" ["R1"; "R4"]
    colors ["Red"; "Blue"; "Green"]
}
```

**Backend Abstraction**: Unified interface for all quantum execution environments
```fsharp
// Synchronous execution
let solve (backend: IQuantumBackend option) problem =
    let actualBackend = backend |> Option.defaultValue (LocalBackendFactory.createUnified())
    actualBackend.ExecuteToState circuit

// Async execution (Task-based, with CancellationToken)
open System.Threading
open System.Threading.Tasks

let solveAsync (backend: IQuantumBackend option) problem (ct: CancellationToken) =
    task {
        let actualBackend = backend |> Option.defaultValue (LocalBackendFactory.createUnified())
        return! actualBackend.ExecuteToStateAsync circuit ct
    }
```

> **Async API (v2025.6+)**: All `IQuantumBackend` implementations now provide `ExecuteToStateAsync` and `ApplyOperationAsync` methods that return `Task<Result<QuantumState, QuantumError>>` with `CancellationToken` support. The async variants are ideal for cloud backends where I/O latency is significant. See [API Reference](api-reference.md) for full signatures.

**Result Type Pattern**: Explicit error handling
```fsharp
match GraphColoring.solve problem 3 None with
| Ok solution -> 
    // Process successful result
    printfn "Colors used: %d" solution.ColorsUsed
| Error err -> 
    // Handle error gracefully
    printfn "Error: %s" err.Message
```

## Extending the Library

**Add a High-Level Builder** (Recommended):
1. Create `Builders/NewProblem.fs`
2. Define computation expression for problem specification
3. Encode to QUBO/Ising model
4. Use existing QAOA/VQE solver
5. Add to C# interop if needed

**Add a Quantum Algorithm**:
1. Create `Algorithms/NewAlgorithm.fs`
2. Build quantum circuit using gate operations
3. Accept `IQuantumBackend` parameter
4. Follow `QuantumFourierTransform.fs` pattern

**Add a Backend**:
1. Create `Backends/NewBackend.fs`
2. Implement `IQuantumBackend` interface (both sync and async members):
   - `ExecuteToState` / `ExecuteToStateAsync` (with `CancellationToken`)
   - `ApplyOperation` / `ApplyOperationAsync` (with `CancellationToken`)
   - `InitializeState`, `SupportsOperation`, `Name`, `NativeStateType`
3. Handle provider-specific circuit format (or use OpenQASM)
4. Add authentication and job submission logic
5. Async methods should use `task { }` computation expression (not `async { }`)
6. See `Backends/CloudBackends.fs` for a reference cloud implementation

## References

- [Getting Started](getting-started.md)
- [Local Simulation](local-simulation.md)
- [Backend Switching](backend-switching.md)
- [Error Mitigation](error-mitigation.md) - ZNE, PEC, REM strategies for NISQ hardware
- [API Reference](api-reference.md)
