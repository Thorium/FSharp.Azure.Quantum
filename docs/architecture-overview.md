# Architecture Overview

## Design Philosophy

FSharp.Azure.Quantum is a **hybrid quantum-classical library**:

- **Classical solvers** - Fast CPU algorithms for small problems (< 50 variables)
- **Quantum solvers** - QAOA/VQE algorithms for large problems via quantum backends
- **Hybrid orchestration** - Automatic routing based on problem size and structure

## Three-Layer Architecture

```
LAYER 1: Hybrid Orchestration
  └─ HybridSolver → Routes to classical or quantum

LAYER 2A: Classical Solvers       LAYER 2B: Quantum Solvers
  ├─ TspSolver (CPU only)            ├─ QuantumTspSolver (needs backend)
  └─ PortfolioSolver (CPU only)      └─ QuantumChemistry (needs backend)

LAYER 3: Execution Backends
  ├─ LocalSimulator (CPU, ≤10 qubits)
  ├─ IonQBackend (Azure Quantum)
  └─ RigettiBackend (Azure Quantum)
```

## Key Concepts

### Classical vs Quantum Solvers

**Classical Solvers** (`Solvers/Classical/`):
- Use CPU algorithms (Nearest Neighbor, Greedy, 2-opt)
- **No backend parameter** - execute directly on CPU
- Fast (milliseconds), free
- Example: `TspSolver.solve distances config`

**Quantum Solvers** (`Solvers/Quantum/`):
- Use quantum algorithms (QAOA, VQE)
- **Require backend parameter** - submit circuits to quantum hardware
- Slower (seconds to minutes), ~$10-100 per run
- Example: `QuantumTspSolver.solve backend distances 1000`

### Why Both?

Small problems (< 50 variables) → Classical is faster and cheaper
Large problems (> 100 variables) → Quantum may have advantage
Use `HybridSolver` to decide automatically.

## Folder Structure

```
src/FSharp.Azure.Quantum/
├── Core/              - Foundation (types, auth, QAOA)
├── LocalSimulator/    - CPU-based quantum simulation
├── Backends/          - IonQ, Rigetti integration
├── Solvers/
│   ├── Classical/     - CPU algorithms (NO backend param)
│   ├── Quantum/       - Quantum algorithms (REQUIRES backend)
│   └── Hybrid/        - Routing logic
├── Algorithms/        - Grover, Amplitude Amplification
├── Builders/          - QUBO encoding, circuit construction
├── ErrorMitigation/   - ZNE, PEC, readout error
└── Utilities/         - Performance benchmarking
```

## Common Questions

**Q: Can I execute `TspSolver` on a quantum backend?**

A: No. `TspSolver` uses classical algorithms (no quantum circuit). Use `QuantumTspSolver` for quantum execution.

**Q: Why have classical solvers in a quantum library?**

A: Hybrid approach - classical for small problems, quantum for large. `HybridSolver` routes automatically.

**Q: What's the difference between Algorithm, Solver, and Backend?**

- **Algorithm** - Mathematical approach (QAOA, Grover, Nearest Neighbor)
- **Solver** - Problem-specific implementation (TspSolver, QuantumTspSolver)
- **Backend** - Execution environment (IonQBackend, LocalSimulator)

## Design Patterns

**Builder Pattern**: Fluent API for problem construction
```fsharp
GraphOptimizationBuilder()
    .Nodes(nodes)
    .Edges(edges)
    .Objective(MinimizeTotalWeight)
    .Build()
```

**Backend Abstraction**: Unified interface for all quantum providers
```fsharp
let solve (backend: IQuantumBackend) problem =
    backend.Execute circuit shots
```

**Pre-Flight Validation**: Catch errors before expensive submission
```fsharp
match validateCircuit constraints circuit with
| Ok () -> backend.Execute circuit
| Error errors -> // Fix before submission
```

## Extending the Library

**Add a Classical Solver**:
1. Create `Solvers/Classical/NewSolver.fs`
2. Implement CPU algorithm (NO backend parameter)
3. Add to `HybridSolver` routing

**Add a Quantum Solver**:
1. Create `Solvers/Quantum/QuantumNewSolver.fs`
2. Accept `backend: IQuantumBackend` parameter
3. Build QUBO/circuit, execute via backend
4. Follow `QuantumTspSolver.fs` pattern

**Add a Backend**:
1. Create `Backends/NewBackend.fs`
2. Implement `IQuantumBackend` interface
3. Handle provider-specific circuit format

## References

- [Getting Started](getting-started.md)
- [Local Simulation](local-simulation.md)
- [Backend Switching](backend-switching.md)
- [API Reference](api-reference.md)
