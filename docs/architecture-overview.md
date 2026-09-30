# Architecture Overview

## Design Philosophy

FSharp.Azure.Quantum is a **quantum-first optimization library** with an opt-in classical path:

- **Quantum solvers (Primary)** - QAOA/VQE algorithms for optimization problems via quantum backends (LocalBackend or Azure Quantum)
- **Classical solvers (Opt-in)** - CPU heuristics used only when you go through `HybridSolver` and the problem is below the advisor's thresholds (50 variables by default)
- **Hybrid orchestration** - HybridSolver routes based on problem size, whether you supplied a backend, and an optional cost budget

**Philosophy**: Quantum algorithms are the primary approach. The quantum solvers never fall back to classical silently; classical solving happens only where you ask for it.

## Four-Layer Quantum-First Architecture

```
LAYER 1: User-Facing API
  ├─ Business Builders (SocialNetworkAnalyzer, ConstraintScheduler, CoverageOptimizer, etc.) → Domain-specific computation expressions
  ├─ High-Level Builders (GraphColoring, MaxCut, Knapsack, TSP, Portfolio, NetworkFlow, TaskScheduling) → Quantum computation expressions / createProblem helpers
  └─ HybridSolver API (Optional) → Quantum/classical routing for TSP, Portfolio, MaxCut, Knapsack, Graph Coloring

LAYER 2: Problem Solvers
  ├─ Quantum Solvers (Primary, Solvers/Quantum)
  │   ├─ QAOA-based (GraphColoring, MaxCut, Knapsack, TSP, Portfolio, NetworkFlow, VertexCover, Clique, SetCover, SAT, Matching, BinPacking, BinaryILP)
  │   ├─ VQE (Quantum Chemistry)
  │   ├─ QFT-based (Arithmetic, Shor's Factorization, Phase Estimation)
  │   └─ Building blocks (Grover, Amplitude Amplification, QFT, QPE, HHL in Algorithms/)
  │
  └─ Classical Solvers (only via HybridSolver)
      ├─ TspSolver (Nearest Neighbor, 2-opt)
      ├─ PortfolioSolver (Greedy Ratio)
      └─ Classical MaxCut / Knapsack / Graph Coloring heuristics inside the quantum solver modules

LAYER 3: Problem Decomposition
  └─ ProblemDecomposition (Core/ProblemDecomposition.fs)
      ├─ Used by the VertexCover, Clique, SetCover, SAT, Matching, BinPacking and BinaryILP solvers
      ├─ Splits a problem that exceeds the backend's MaxQubits into sub-problems (e.g. connected components)
      └─ Recombines the sub-solutions

LAYER 4: Execution Backends
  ├─ LocalBackend (CPU state-vector simulation, memory-derived width ≤30, default)
  ├─ Cloud Backends (CloudBackends.CloudBackendFactory)
  │   ├─ RigettiCloudBackend (superconducting qubits)
  │   ├─ IonQCloudBackend (trapped ions)
  │   ├─ QuantinuumCloudBackend (trapped ions)
  │   ├─ AtomComputingCloudBackend (neutral atoms)
  │   └─ IqmCloudBackend (superconducting qubits)
  ├─ D-Wave annealers (DWaveBackend mock, RealDWaveBackend)
  ├─ Topological backend (separate FSharp.Azure.Quantum.Topological package)
  ├─ AWS Braket backend (separate FSharp.Azure.Quantum.Braket package)
  └─ All backends implement IQuantumBackend (sync + Task-based async)
```

**Key Architectural Principle**: High-level builders go directly to quantum solvers. Classical solvers are only reached through HybridSolver.

**Architecture Flow**:
- High-level builders: Builder → Quantum solver → QaoaExecutionHelpers / QaoaCircuit → Backend
- Business builders: Business Builder → Quantum solver → ProblemDecomposition → QAOA → Backend

## Key Concepts

### Quantum-First Approach

**Business Builders** (`SocialNetworkAnalyzer`, `ConstraintScheduler`, `CoverageOptimizer`, `ResourcePairing`, `PackingOptimizer`):
- Domain-specific computation expression builders in the `Business/` folder
- Encode real-world business problems into the quantum optimization pipeline
- Internally delegate to the quantum solvers (clique, vertex cover, matching, set cover, SAT, bin packing)
- Flow: Business Builders → Solvers → ProblemDecomposition → Backend

**High-Level Builders** (`GraphColoring`, `MaxCut`, `TSP`, `Portfolio`, etc.):
- Use quantum algorithms directly (QAOA)
- Take an optional backend: `None` means a new LocalBackend (simulation)
- Recommended whenever the problem fits the backend
- Example: `GraphColoring.solve problem 4 None` → LocalBackend (default)

**Classical Solvers** (`TspSolver`, `PortfolioSolver`, and the classical paths of the MaxCut/Knapsack/Graph Coloring solvers):
- Use CPU heuristics (Nearest Neighbor, Greedy, 2-opt)
- **Only reached via HybridSolver** - their solve functions are internal
- Used for problems below the advisor's thresholds, or when forced with `Some HybridSolver.Classical`
- Example: `HybridSolver.solveTsp distances None None None` → Routes automatically

### Why Both?

**Direct Quantum (Recommended for quantum work)**:
- Consistent API across problem types
- Leverages quantum algorithms (QAOA/VQE/QFT)
- LocalBackend provides free simulation (width derived from available memory)

**HybridSolver (Optional)**:
- Solves small problems with classical heuristics
- Saves quantum circuit overhead for problems too small to benefit
- Transparent routing with reasoning provided
- Use when problem sizes vary and small cases should stay classical

### Builder Routing Architecture

High-Level Builders (`GraphColoring`, `MaxCut`, `TSP`, `Portfolio`, ...) provide a business-friendly quantum API that encodes problems as QUBO/Ising models and solves them using QAOA. Business Builders (`SocialNetworkAnalyzer`, `ConstraintScheduler`, etc.) provide domain-specific APIs that delegate to the quantum solvers.

**Direct Quantum Routing (Default):**
```
User → GraphColoring.solve problem numColors backend
         ↓
       Encode as QUBO
         ↓
       Build QAOA Circuit
         ↓
       Execute on Backend (LocalBackend default)
         ↓
       Decode Bitstring → Color Assignments
         ↓
       Return QuantumResult<ColoringSolution>
```

Problems that need more qubits than the backend offers return an `Error` naming the qubit count; only the solvers behind the business builders decompose automatically.

**HybridSolver Routing (Optional):**
```
User → HybridSolver.solveGraphColoring problem numColors budget timeout forceMethod
         ↓
       forceMethod set? → run that method
         ↓
       QuantumAdvisor: size < 50 (default thresholds)? → Classical
                       size ≥ 50 and a backend given? → Quantum
                       (estimated cost over budget → Classical)
         ↓
       Execute chosen method
         ↓
       Return QuantumResult<HybridSolver.Solution<_>> with Method + Reasoning
```

The plain `solveX` functions pass no backend, so they only use quantum when forced; use the `solveXWithBackend` variants to let the advisor route to quantum.

**Benefits:**
- ✅ **Direct Builders**: Simple quantum API, consistent across problem types
- ✅ **HybridSolver**: Classical answers for small problems, transparent reasoning
- ✅ **Type-safe**: `QuantumResult<'T>` (= `Result<'T, QuantumError>`) for error handling
- ✅ **Backend abstraction**: LocalBackend (simulation) or cloud backends (IonQ, Rigetti, Quantinuum, Atom Computing, IQM)

**Example:**
```fsharp
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.GraphColoring
open FSharp.Azure.Quantum.Quantum

// Direct Quantum Approach (Recommended) - consistent API
let problem = graphColoring {
    node "R1" ["R2"; "R3"]
    node "R2" ["R1"; "R4"]
    node "R3" ["R1"]
    node "R4" ["R2"]
    colors ["Red"; "Green"; "Blue"]
}
match GraphColoring.solve problem 3 None with  // None = LocalBackend
| Ok solution -> printfn "Colors Used: %d" solution.ColorsUsed
| Error err -> printfn "Error: %s" err.Message

// HybridSolver (Optional) takes the solver-level problem type
let hybridProblem: QuantumGraphColoringSolver.GraphColoringProblem =
    { Vertices = ["R1"; "R2"; "R3"; "R4"]
      Edges =
        [ GraphOptimization.edge "R1" "R2" 1.0
          GraphOptimization.edge "R1" "R3" 1.0
          GraphOptimization.edge "R2" "R4" 1.0 ]
      NumColors = 3
      FixedColors = Map.empty }

match HybridSolver.solveGraphColoring hybridProblem 3 None None None with
| Ok solution -> 
    printfn "Method: %A" solution.Method  // Shows Classical or Quantum
    printfn "Reasoning: %s" solution.Reasoning
    printfn "Colors Used: %d" solution.Result.ColorsUsed
| Error err -> printfn "Error: %s" err.Message
```

## Folder Structure

```
src/FSharp.Azure.Quantum/
├── Core/              - Foundation (errors, auth, IQuantumBackend, QAOA circuit/optimizer, cost estimation)
│   └── ProblemDecomposition.fs - Backend-aware problem decomposition and partitioning
├── LocalSimulator/    - CPU-based state-vector simulation (StateVector, Gates, Measurement)
├── Backends/          - LocalBackend, cloud backends (Rigetti, IonQ, Quantinuum, AtomComputing, IQM), D-Wave
│   └── CloudBackends.fs - Cloud backends + CloudBackendFactory
├── Data/              - Molecule library, periodic table, chemistry and financial data providers
├── MachineLearning/   - Quantum ML (VQC, QuantumKernel, QuantumKernelSVM, HHL regression)
├── Solvers/
│   ├── Classical/     - High-level builders (GraphColoring, MaxCut, Knapsack, TSP, Portfolio, NetworkFlow)
│   │                    plus the classical TspSolver/PortfolioSolver used by HybridSolver
│   ├── Quantum/       - Quantum solvers (QAOA, VQE, QFT-based) and the advanced builders
│   └── Hybrid/        - Optional routing logic (ProblemAnalysis, QuantumAdvisor, HybridSolver)
├── TaskScheduling/    - scheduledTask / resource / scheduling builders and solvers
├── Algorithms/        - Grover, Amplitude Amplification, QFT, QPE, Shor, HHL, Trotter-Suzuki, QRNG, ...
├── Builders/          - Circuit-level tooling (CircuitBuilder, transpiler, qubit routing, OpenQASM, QIR, noise models)
├── Business/          - Domain-specific computation expression builders
│   ├── SocialNetworkAnalyzer.fs  - Community detection and influence optimization
│   ├── ConstraintScheduler.fs    - Constraint-based scheduling optimization
│   ├── CoverageOptimizer.fs      - Set cover and facility location problems
│   ├── ResourcePairing.fs        - Bipartite matching and resource assignment
│   └── PackingOptimizer.fs       - Bin packing and knapsack variants
├── ErrorMitigation/   - ZNE, PEC, REM (reduce quantum noise)
├── Visualization/     - ASCII and Mermaid renderers for circuits and solutions
└── Utilities/         - Performance benchmarking
```

The topological and AWS Braket backends live in separate projects, `src/FSharp.Azure.Quantum.Topological` and `src/FSharp.Azure.Quantum.Braket`.

## Common Questions

**Q: Should I use the high-level builders or HybridSolver?**

A: **Use high-level builders directly** (`GraphColoring.solve`, `MaxCut.solve`, etc.) when you want quantum execution. They provide:
- Consistent quantum API across problem types
- LocalBackend simulation (free, memory-derived width)

Use HybridSolver if small problems should be solved classically (below 50 variables with the default thresholds) and quantum used only for large ones with a backend you supply.

**Q: Can I access classical solvers directly?**

A: Not their solve functions: those in `TspSolver` and `PortfolioSolver` are internal and reached via `HybridSolver` (pass `Some HybridSolver.Classical` to force the classical path). Helpers such as `TspSolver.buildDistanceMatrix` and `TspSolver.calculateTourLength` are public.

**Q: What's the difference between Algorithm, Solver, Builder, and Backend?**

- **Algorithm** - Mathematical approach (QAOA, Grover, QFT, Shor)
- **Solver** - Problem-specific implementation (uses algorithms internally)
- **ProblemDecomposition** - Backend-aware partitioning layer that splits large problems into sub-problems matching backend constraints
- **Builder** - User-facing API with computation expressions (e.g., `graphColoring { ... }`)
- **Business Builder** - Domain-specific builder for real-world problems (e.g., `SocialNetworkAnalyzer`, `ConstraintScheduler`)
- **Backend** - Execution environment (LocalBackend, IonQCloudBackend, RigettiCloudBackend, ...)

## Design Patterns

**Computation Expression Pattern**: Fluent, type-safe problem construction
```fsharp
let coloringProblem = graphColoring {
    node "R1" ["R2"; "R3"]
    node "R2" ["R1"; "R4"]
    node "R3" ["R1"]
    node "R4" ["R2"]
    colors ["Red"; "Blue"; "Green"]
}
```

**Backend Abstraction**: Unified interface for all quantum execution environments
```fsharp
open System.Threading
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Backends

// A Bell-state circuit, wrapped as an ICircuit
let circuit =
    CircuitBuilder.empty 2
    |> CircuitBuilder.addGate (CircuitBuilder.Gate.H 0)
    |> CircuitBuilder.addGate (CircuitBuilder.Gate.CNOT(0, 1))
    |> CircuitAbstraction.wrapCircuit

// Synchronous execution
let run (backend: IQuantumBackend option) =
    let actualBackend = backend |> Option.defaultValue (LocalBackendFactory.createUnified())
    actualBackend.ExecuteToState circuit

// Async execution (Task-based, with CancellationToken)
let runAsync (backend: IQuantumBackend option) (ct: CancellationToken) =
    task {
        let actualBackend = backend |> Option.defaultValue (LocalBackendFactory.createUnified())
        return! actualBackend.ExecuteToStateAsync circuit ct
    }
```

> **Async API**: All `IQuantumBackend` implementations provide `ExecuteToStateAsync` and `ApplyOperationAsync` methods that return `Task<Result<QuantumState, QuantumError>>` with `CancellationToken` support. The async variants suit cloud backends where I/O latency is significant. See [API Reference](api-reference) for full signatures.

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
1. Add a quantum solver under `Solvers/Quantum/` that encodes the problem as a QUBO/Ising model and runs QAOA (see `QuantumMaxCutSolver.fs`)
2. Add the domain builder (computation expression or `createProblem` helpers and `solve`) next to the existing ones, e.g. `Solvers/Classical/MaxCutBuilder.fs`
3. Use `ProblemDecomposition` if the problem can be split when it exceeds the backend's qubits
4. Add to C# interop (`Builders/BuildersCSharpExtensions.fs`) if needed

**Add a Quantum Algorithm**:
1. Create `Algorithms/NewAlgorithm.fs`
2. Express the algorithm as intent → plan → execute (see the [Intent-First Algorithms ADR](adr-intent-first-algorithms))
3. Accept an `IQuantumBackend` parameter
4. Follow the `Algorithms/QFT.fs` pattern, including its whole-circuit fallback: when `UnifiedBackend.isIncrementalUnsupported` reports that the backend (cloud hardware) refuses `ApplyOperation`, submit the complete gate list from |0…0⟩ with `UnifiedBackend.submitAsCircuit`, or wrap state-independent code in `WholeCircuit.run`

**Add a Backend**:
1. Create `Backends/NewBackend.fs`
2. Implement `IQuantumBackend` interface (both sync and async members):
   - `ExecuteToState` / `ExecuteToStateAsync` (with `CancellationToken`)
   - `ApplyOperation` / `ApplyOperationAsync` (with `CancellationToken`)
   - `InitializeState`, `SupportsOperation`, `Name`, `NativeStateType`
   - Optionally `IQubitLimitedBackend.MaxQubits` so solvers can check capacity up front
3. Handle provider-specific circuit format (or use OpenQASM); transpile to the provider's gates first (`CloudBackendHelpers.transpileForTarget`)
4. Add authentication and job submission logic. A backend that runs complete circuits only returns `OperationError("ApplyOperation", "... incremental ...")` from `ApplyOperation`, so the algorithms switch to whole-circuit submission; it implements `IShotSamplingBackend` (its shots per circuit) and `IJobCountingBackend` (a `JobBudget` reserved before every submission)
5. Async methods should use `task { }` computation expression (not `async { }`)
6. See `Backends/CloudBackends.fs` for a reference cloud implementation

## References

- [Getting Started](getting-started.md)
- [Local Simulation](local-simulation.md)
- [Backend Switching](backend-switching.md)
- [Error Mitigation](error-mitigation.md) - ZNE, PEC, REM strategies for NISQ hardware
- [API Reference](api-reference.md)
