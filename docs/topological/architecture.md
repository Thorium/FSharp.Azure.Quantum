# Topological Quantum Computing - Architecture Guide

## Overview

The FSharp.Azure.Quantum.Topological library follows a **strictly layered architecture** that separates concerns and enables composition. This architecture mirrors the gate-based quantum computing library, implementing a **fundamentally different paradigm** while integrating with it via the shared `IQuantumBackend` interface.

## Architectural Layers

```
┌─────────────────────────────────────────────────────────┐
│  Layer 6: Builders, Formats & Utilities                 │
│  User-friendly DSL, file formats, helpers               │
│  Files: TopologicalBuilder.fs, TopologicalFormat.fs,    │
│         Visualization.fs, NoiseModels.fs,               │
│         TopologicalHelpers.fs, TopologicalError.fs      │
└─────────────────────────────────────────────────────────┘
                           ▼
┌─────────────────────────────────────────────────────────┐
│  Layer 5: Compilation & Integration                     │
│  Gate-to-braid conversion, optimization, algorithm ext. │
│  Files: GateToBraid.fs, BraidToGate.fs,                 │
│         SolovayKitaev.fs, CircuitOptimization.fs,       │
│         AlgorithmExtensions.fs                          │
└─────────────────────────────────────────────────────────┘
                           ▼
┌─────────────────────────────────────────────────────────┐
│  Layer 4: Algorithms                                     │
│  Topological-specific algorithms & error correction     │
│  Files: MagicStateDistillation.fs, ToricCode.fs,        │
│         ErrorPropagation.fs                             │
└─────────────────────────────────────────────────────────┘
                           ▼
┌─────────────────────────────────────────────────────────┐
│  Layer 3: Operations (High-Level)                       │
│  Qubit encoding, braiding sequences, measurement        │
│  Files: TopologicalOperations.fs, FusionTree.fs         │
└─────────────────────────────────────────────────────────┘
                           ▼
┌─────────────────────────────────────────────────────────┐
│  Layer 2: Backends (Execution)                          │
│  Unified IQuantumBackend                                │
│  Files: TopologicalBackend.fs                           │
└─────────────────────────────────────────────────────────┘
                           ▼
┌─────────────────────────────────────────────────────────┐
│  Layer 1: Core (Mathematical Foundation)                │
│  Pure functions: anyon species, fusion, braiding, knots │
│  Files: AnyonSpecies.fs, FusionRules.fs,                │
│         BraidingOperators.fs, FMatrix.fs, RMatrix.fs,   │
│         ModularData.fs, BraidGroup.fs,                  │
│         BraidingConsistency.fs, EntanglementEntropy.fs, │
│         KauffmanBracket.fs, KnotConstructors.fs         │
└─────────────────────────────────────────────────────────┘
```

## Layer Descriptions

### Layer 1: Core - Mathematical Foundation

**Purpose:** Pure mathematical primitives with no side effects or I/O.

**Key Modules:**
- `AnyonSpecies.fs` - Particle types, quantum dimensions
- `FusionRules.fs` - Fusion algebra (σ×σ=1+ψ)
- `BraidingOperators.fs` - R-matrices, F-matrices

**Design Principles:**
- ✅ Pure functions only (no Task, no Async, no side effects)
- ✅ Total functions (no exceptions for valid inputs)
- ✅ Immutable data structures
- ✅ RequireQualifiedAccess for all modules

**Example:**
```fsharp
// Pure function - no side effects
let channels = FusionRules.channels sigma sigma Ising
// Result: [Vacuum; Psi] (deterministic, no I/O)
```

### Layer 2: Backends - Execution Abstraction

**Purpose:** Abstract execution of topological quantum operations across different backends (simulator, hardware).

**Key Interfaces:**
- `IQuantumBackend` (unified) - Shared interface with gate-based library, enabling standard algorithms to run on topological backends

**Implementations:**
- `TopologicalUnifiedBackend` - Primary backend implementing `IQuantumBackend`, with automatic gate-to-braid compilation
- `TopologicalUnifiedBackendFactory` - Factory functions: `createIsing`, `createFibonacci`, `create`

**Design Principles:**
- ✅ Interface-based design (dependency inversion)
- ✅ Capabilities-based validation
- ✅ Unified backend enables standard algorithms (Grover, QFT, Shor, HHL) on topological hardware
- ✅ Backend-specific details hidden from consumers

**Example:**
```fsharp
// Unified backend (recommended)
let backend = TopologicalUnifiedBackendFactory.createIsing 10
let! state = backend.InitializeState 4  // Returns Result
```

### Layer 3: Operations - High-Level Quantum Operations

**Purpose:** Build meaningful quantum operations on top of backends.

**Key Modules:**
- `TopologicalOperations.fs` - Braiding, measurement, superposition
- `FusionTree.fs` - State representation, tree manipulation

**Design Principles:**
- ✅ Composable operations (small, focused functions)
- ✅ Backend-agnostic (works with any IQuantumBackend)
- ✅ Type-safe state management
- ✅ Clear separation of concerns

**Example:**
```fsharp
// High-level operation composed from backend primitives
let applyGate backend state = task {
    let! s1 = backend.Braid 0 state
    let! s2 = backend.Braid 2 s1
    return s2
}
```

### Layer 4: Algorithms - Domain-Specific Algorithms

**Purpose:** Implement algorithms specific to topological quantum computing.

**Implemented:**
- Magic state distillation (15-to-1 protocol for Ising universality)
- Toric code error correction (syndrome detection and correction)
- Error propagation analysis through topological circuits
- Kauffman bracket and Jones polynomial calculations (via KauffmanBracket + KnotConstructors)

**Note:** Standard quantum algorithms (Grover, QFT, Shor, HHL) run on topological backends via `AlgorithmExtensions` (Layer 5), not as topological-native algorithms.

**Design Principles:**
- ✅ Algorithm = sequence of Layer 3 operations
- ✅ Backend-parametric (work with any backend)
- ✅ Well-documented complexity and resource requirements
- ✅ Unit-tested against theoretical predictions

**Example:**
```fsharp
// Topological-specific algorithm
let calculateKnot backend braidPattern = task {
    let! state = backend.Initialize Ising 6
    // Apply braiding to form knot...
    let! (outcome, prob) = measureReannihilation state
    return prob  // ∝ |Kauffman invariant|²
}
```

### Layer 5: Compilation & Integration

**Purpose:** Convert between gate-based and topological representations, optimize circuits, and enable standard algorithms on topological backends.

**Key Modules:**
- `GateToBraid.fs` - Convert gate-based circuits to braid sequences (21 gate types)
- `BraidToGate.fs` - Convert braid sequences back to gate operations
- `SolovayKitaev.fs` - Gate approximation for efficient braid decomposition
- `CircuitOptimization.fs` - Braid sequence optimization and simplification
- `AlgorithmExtensions.fs` - Run Grover, QFT, Shor, HHL on topological backends

**Design Principles:**
- ✅ Transparent gate-to-braid compilation
- ✅ Algorithm extensions delegate to standard implementations (zero code duplication)
- ✅ Solovay-Kitaev approximation for non-Clifford gates
- ✅ Well-documented approximation error tracking

**Example:**
```fsharp
// Standard algorithms work on topological backends
let backend = TopologicalUnifiedBackendFactory.createIsing 20
let result = AlgorithmExtensions.searchSingleWithTopology 42 8 backend config
let qft = AlgorithmExtensions.qftWithTopology 4 backend qftConfig
```

### Layer 6: Builders, Formats & Utilities

**Purpose:** Provide user-friendly DSL for composing quantum programs, file formats, and supporting utilities.

**Key Modules:**
- `TopologicalBuilder.fs` - Computation expression builder (`topological backend { ... }`)
- `TopologicalFormat.fs` - `.tqp` file format for serializing topological programs
- `NoiseModels.fs` - Configurable noise simulation for realistic error modeling
- `Visualization.fs` - State visualization and debugging utilities
- `TopologicalHelpers.fs` - Complex number utilities and particle display formatting
- `TopologicalError.fs` - Error types, TopologicalResult, and result computation expression

**Design Principles:**
- Familiar F# syntax (computation expressions)
- Type-safe composition
- Automatic resource management
- Clear error messages

**Example:**
```fsharp
let program = topological backend {
    let! state = initialize Ising 4
    do! braid 0
    do! braid 2
    let! outcome = measure 0
    return outcome
}
```

## Relationship with Gate-Based Library

### Different Paradigms, Shared Interface

**Fundamental Differences:**

| Aspect | Gate-Based | Topological |
|--------|-----------|-------------|
| **Operations** | H, CNOT, Rz gates | Braiding anyons |
| **State** | Amplitude vectors | Fusion trees |
| **Qubits** | Direct 2-state | Encoded in anyon clusters |
| **Measurement** | Z-basis {|0>, |1>} | Fusion outcomes {1, sigma, psi} |
| **Algorithms** | Shor's, HHL, VQE | Kauffman, topological codes |

### Integration via IQuantumBackend

While the paradigms differ, the topological backend implements `IQuantumBackend` from the gate-based library. This enables:
- Standard algorithms (Grover, QFT, Shor, HHL) to run on topological backends
- Automatic gate-to-braid compilation (transparent to the caller)
- Backend-agnostic algorithm implementation

### Shared Patterns (Same Structure, Different Content)

```
Gate-Based:                    Topological:
├── Core/                      ├── Core/
│   ├── Gates.fs              │   ├── AnyonSpecies.fs
│   └── StateVector.fs        │   ├── FusionRules.fs
├── Backends/                  ├── Backends/
│   ├── IQuantumBackend       │   ├── IQuantumBackend (shared!)
│   └── LocalBackend          │   └── TopologicalUnifiedBackend
├── Algorithms/                ├── Algorithms/
│   ├── Shor.fs               │   ├── MagicStateDistillation.fs
│   └── Grover.fs             │   └── AlgorithmExtensions.fs
└── Builders/                  └── Builders/
    └── QuantumBuilder            └── TopologicalBuilder
```

## Design Principles

### 1. Idiomatic F#

✅ **Immutability by default**
```fsharp
// ✅ GOOD: Immutable state transformation
let evolvedState = braidSuperposition 0 state

// ❌ BAD: Mutable state
state.Braid(0)  // Don't mutate!
```

✅ **Pattern matching over inheritance**
```fsharp
// ✅ GOOD: Discriminated union
type Particle = Vacuum | Sigma | Psi | Tau

// ❌ BAD: Class hierarchy
type Particle() = ...
type Sigma() = inherit Particle()
```

✅ **Functions over methods**
```fsharp
// ✅ GOOD: Module with functions
module FusionRules =
    let channels a b theory = ...

// ❌ BAD: Static class
type FusionRules() =
    static member Channels(a, b, theory) = ...
```

✅ **Composition over configuration**
```fsharp
// ✅ GOOD: Compose small functions
let applyGate = braid 0 >> braid 2 >> braid 0

// ❌ BAD: Configuration object
type GateConfig() =
    member val Braid1 = 0
    member val Braid2 = 2
```

### 2. Type Safety

✅ **Domain-driven types**
```fsharp
type AnyonType = Ising | Fibonacci  // Not just strings!
type Particle = Vacuum | Sigma | Psi | Tau
```

✅ **Option for optional values**
```fsharp
type State = {
    Tree: Tree
    MeasurementOutcome: Particle option  // Not null!
}
```

✅ **Result for errors**
```fsharp
let validateCapabilities backend required : Result<unit, string> =
    if meetsRequirements then Ok ()
    else Error "Backend lacks braiding support"
```

### 3. Testability

✅ **Business-meaningful assertions**
```fsharp
[<Fact>]
let ``Two sigma anyons create 2D Hilbert space (1 qubit)`` () =
    // Not just: Assert.Equal(2, dimension)
    // But: Clear business meaning!
```

✅ **Pure functions are inherently testable**
```fsharp
// No mocking needed - pure function!
let result = FusionRules.fuse Sigma Sigma Ising
Assert.Equal([Vacuum; Psi], result)
```

### 4. Performance

✅ **Lazy evaluation where appropriate**
```fsharp
// Don't enumerate all fusion trees unless needed
let allTrees = FusionTree.enumerate particles charge |> Seq.cache
```

✅ **Tail recursion for large computations**
```fsharp
let rec braidSequence acc index state =
    if index >= length then acc
    else braidSequence (braid index acc) (index + 1) state
```

## Future Extensions

### Planned Layers

1. **Hardware Adapters** (Layer 2 Extensions)
   - Microsoft Majorana Gen 1 backend
   - Other topological hardware experiments

2. **Advanced Algorithms** (Layer 4 Extensions)
   - Surface code variants (planar, color codes)
   - Fibonacci anyon native gate compilation

3. **Performance Optimizations**
   - GPU acceleration
   - Sparse matrices
   - Parallel braiding

### Integration Points

**Current Bridges:**
- Gate-to-braid compilation (GateToBraid, 21 gate types)
- Standard algorithm integration (AlgorithmExtensions - Grover, QFT, Shor, HHL)
- Shared IQuantumBackend interface

**Potential Future Bridges:**
- Error correction code interop
- Hybrid topological + gate-based computing pipelines

## References

- Steven Simon, "Topological Quantum" (2023)
  - Chapters 8-10: Fusion and braiding theory
  - Chapter 11: Computing with anyons
  - Chapters 26-31: Error correction

- Microsoft Majorana Documentation
  - Ising anyons (SU(2)₂)
  - Hardware specifications

- Kauffman, "Knots and Physics" (1991)
  - Kauffman bracket invariants

## Contributing

When adding new features, **always**:

1. ✅ Place in correct layer (see diagram above)
2. ✅ Follow idiomatic F# principles
3. ✅ Write business-meaningful tests
4. ✅ Update this architecture document
5. ✅ Keep topological-specific code in this package (shared interfaces live in the gate-based library)

**Remember:** We're building a **companion library** that follows the **same architectural pattern** as the gate-based library, sharing the `IQuantumBackend` interface for interoperability!
