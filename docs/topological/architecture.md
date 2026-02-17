# Topological Quantum Computing - Architecture Guide

## Overview

The FSharp.Azure.Quantum.Topological library follows a **strictly layered architecture** that separates concerns and enables composition. This architecture mirrors the gate-based quantum computing library but is **fundamentally separate** because topological quantum computing is a different paradigm.

## Architectural Layers

```
┌─────────────────────────────────────────────────────────┐
│  Layer 6: Business Domain                               │
│  Real-world applications (error detection, etc.)        │
│  Files: (Future) Business/TopologicalApplications.fs    │
└─────────────────────────────────────────────────────────┘
                           ▼
┌─────────────────────────────────────────────────────────┐
│  Layer 5: Builders (Computation Expressions)            │
│  User-friendly DSL for composing quantum programs       │
│  Files: TopologicalBuilder.fs                           │
└─────────────────────────────────────────────────────────┘
                           ▼
┌─────────────────────────────────────────────────────────┐
│  Layer 4: Algorithms                                     │
│  Topological-specific algorithms                        │
│  Files: MagicStateDistillation.fs, (Future) Kauffman    │
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
│  Simulator, hardware adapters (via ITopologicalBackend) │
│  Files: TopologicalBackend.fs                           │
└─────────────────────────────────────────────────────────┘
                           ▼
┌─────────────────────────────────────────────────────────┐
│  Layer 1: Core (Mathematical Foundation)                │
│  Pure functions: anyon species, fusion, braiding        │
│  Files: AnyonSpecies.fs, FusionRules.fs,                │
│         BraidingOperators.fs                            │
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

**Key Interface:**
- `ITopologicalBackend` - Unified interface for all backends

**Implementations:**
- `SimulatorBackend` - Classical simulation of topological quantum operations

**Design Principles:**
- ✅ Interface-based design (dependency inversion)
- ✅ Capabilities-based validation
- ✅ Async operations (Task-based)
- ✅ Backend-specific details hidden from consumers

**Example:**
```fsharp
let backend: ITopologicalBackend = createSimulator Ising 10
let! state = backend.Initialize Ising 4  // Returns Task
```

### Layer 3: Operations - High-Level Quantum Operations

**Purpose:** Build meaningful quantum operations on top of backends.

**Key Modules:**
- `TopologicalOperations.fs` - Braiding, measurement, superposition
- `FusionTree.fs` - State representation, tree manipulation

**Design Principles:**
- ✅ Composable operations (small, focused functions)
- ✅ Backend-agnostic (works with any ITopologicalBackend)
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

**Note:** These are **NOT** gate-based algorithms (HHL, Shor's). Topological algorithms are fundamentally different!

**Examples:**
- Kauffman bracket invariant calculation
- Topological error correction protocols
- Fibonacci anyon universal gate compilation

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

### Layer 5: Builders - Computation Expressions

**Purpose:** Provide user-friendly DSL for composing quantum programs.

**Key Builder:**
- `TopologicalProgramBuilder` - Computation expression for topological programs

**Design Principles:**
- Familiar F# syntax (computation expressions)
- Type-safe composition
- Automatic resource management
- Clear error messages

**Example:**
```fsharp
let program = topological backend {
    let! qubit = initialize Ising 4
    do! braid 0 qubit
    do! braid 2 qubit
    let! outcome = measure 0 qubit
    return outcome
}
```

### Layer 6: Business Domain - Real-World Applications

**Purpose:** Map business problems to topological quantum solutions.

**Examples:**
- Quantum error detection
- Cryptographic protocols
- Material simulation (Majorana-specific)

**Design Principles:**
- ✅ Domain-driven design
- ✅ Business-meaningful names
- ✅ Hide quantum complexity from domain experts
- ✅ Clear ROI metrics

**Example:**
```fsharp
// Business problem: Detect errors in quantum memory
let detectMemoryError qubitState = task {
    let! errorPresent = topologicalErrorDetection qubitState
    return if errorPresent then TriggerCorrection else Continue
}
```

## Separation from Gate-Based Library

### Why Separate?

**Fundamental Incompatibility:**

| Aspect | Gate-Based | Topological |
|--------|-----------|-------------|
| **Operations** | H, CNOT, Rz gates | Braiding anyons |
| **State** | Amplitude vectors | Fusion trees |
| **Qubits** | Direct 2-state | Encoded in anyon clusters |
| **Measurement** | Z-basis {│0⟩, │1⟩} | Fusion outcomes {1, σ, ψ} |
| **Algorithms** | Shor's, HHL, VQE | Kauffman, topological codes |

### Shared Patterns (Not Implementation!)

Both libraries follow the **same architectural layers**:

```
Gate-Based:                    Topological:
├── Core/                      ├── Core/
│   ├── Gates.fs              │   ├── AnyonSpecies.fs
│   └── StateVector.fs        │   ├── FusionRules.fs
├── Backends/                  ├── Backends/
│   ├── IQuantumBackend       │   ├── ITopologicalBackend
│   └── LocalBackend          │   └── SimulatorBackend
├── Algorithms/                ├── Algorithms/
│   ├── Shor.fs               │   └── KnotiInvariant.fs
└── Builders/                  └── Builders/
    └── QuantumBuilder            └── TopologicalBuilder
```

**Key Point:** Same **structure**, different **content**!

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
   - IBM topological experiments
   - Google topological prototypes

2. **Advanced Algorithms** (Layer 4 Extensions)
   - Magic state distillation (for universality)
   - Topological error correction codes
   - Fibonacci anyon gate compilation

3. **Business Applications** (Layer 6 Extensions)
   - Quantum cryptography protocols
   - Majorana-based secure storage
   - Material simulation (topological insulators)

### Integration Points

**Potential (Future) Bridges:**
- Toric code simulation of gate-based QC
- Error correction code interop
- High-level problem solvers (shared abstractions)

**NOT Planned:**
- Direct algorithm porting (Shor's → topological)
- Circuit compilation (gates → braiding)
  - Reason: Too much overhead, not practical

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
5. ✅ Keep separate from gate-based library

**Remember:** We're building **two separate libraries** that happen to follow the **same architectural pattern**!
