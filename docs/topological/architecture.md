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
│         DeviceProfile.fs, TopologicalHelpers.fs,        │
│         TopologicalError.fs                             │
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
│         SurfaceCode.fs, AnyonicErrorCorrection.fs,      │
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
- ✅ Total functions (errors come back as `TopologicalResult`, not exceptions)
- ✅ Immutable data structures
- ✅ `RequireQualifiedAccess` on the core modules (`AnyonSpecies`, `FusionRules`, `BraidingOperators`, `FusionTree`, ...)

**Example:**
```fsharp
open FSharp.Azure.Quantum.Topological

// Pure function - no side effects
let sigma = AnyonSpecies.Particle.Sigma
let channels = FusionRules.channels sigma sigma AnyonSpecies.AnyonType.Ising
// Result: Ok [Vacuum; Psi] (deterministic, no I/O)
```

### Layer 2: Backends - Execution Abstraction

**Purpose:** Abstract execution of topological quantum operations across different backends (simulator, hardware).

**Key Interfaces:**
- `IQuantumBackend` (unified) - Shared interface with gate-based library, enabling standard algorithms to run on topological backends

**Implementations:**
- `TopologicalUnifiedBackend` - Classical simulator implementing `IQuantumBackend`, with automatic gate-to-braid compilation
- `TopologicalUnifiedBackendFactory` - Factory functions: `createIsing`, `createFibonacci`, `createUnified`, `create`

**Design Principles:**
- ✅ Interface-based design (dependency inversion)
- ✅ Capabilities-based validation (`SupportsOperation`)
- ✅ Unified backend runs standard algorithms (Grover, QFT, Shor, HHL) on the topological simulator
- ✅ Backend-specific details hidden from consumers

**Example:**
```fsharp
open FSharp.Azure.Quantum.Core.BackendAbstraction

// Unified backend (recommended); the argument caps the number of anyons
let backend = TopologicalUnifiedBackendFactory.createIsing 10

// InitializeState takes logical qubits: 2 qubits = 6 Ising anyons
match backend.InitializeState 2 with
| Ok state -> printfn "Native state type: %A" backend.NativeStateType
| Error err -> printfn "Error: %s" err.Message
```

### Layer 3: Operations - High-Level Quantum Operations

**Purpose:** Build meaningful quantum operations on fusion-tree states. The backend (Layer 2) delegates to these functions.

**Key Modules:**
- `TopologicalOperations.fs` - Braiding, F-moves, fusion measurement, superposition, qubit-level gates
- `FusionTree.fs` - State representation, tree manipulation, computational-basis encoding

**Design Principles:**
- ✅ Composable operations (small, focused functions returning `TopologicalResult`)
- ✅ Work directly on `FusionTree.State` and `TopologicalOperations.Superposition` values
- ✅ Type-safe state management
- ✅ Clear separation of concerns

**Example:**
```fsharp
// High-level operation composed from Layer 3 primitives
let applyGate (state: TopologicalOperations.Superposition) =
    TopologicalOperations.braidSuperposition 0 state
    |> Result.bind (TopologicalOperations.braidSuperposition 2)
```

### Layer 4: Algorithms - Domain-Specific Algorithms

**Purpose:** Implement algorithms specific to topological quantum computing.

**Implemented:**
- Magic state distillation (15-to-1 protocol for Ising universality)
- Toric code error correction (syndrome detection and MWPM decoder)
- Surface code variants (planar code with boundary matching, color code on 4.8.8 lattice with greedy decoder)
- Anyonic error correction (fusion-tree-level charge violation detection, syndrome extraction, greedy charge-correction decoder, code space projection)
- Error propagation analysis through topological circuits
- Kauffman bracket and Jones polynomial calculations (via KauffmanBracket + KnotConstructors)

**Note:** Standard quantum algorithms (Grover, QFT, Shor, HHL) run on topological backends via `AlgorithmExtensions` (Layer 5), not as topological-native algorithms.

**Design Principles:**
- ✅ Built on Layer 1 and Layer 3 types (fusion trees, superpositions)
- ✅ Pure functions; randomness is passed in explicitly (e.g. a `System.Random`)
- ✅ Well-documented complexity and resource requirements
- ✅ Unit-tested against theoretical predictions

**Example:**
```fsharp
// Topological-specific computation: knot invariants from a planar diagram
let trefoil = KnotConstructors.trefoil true
let bracket = KauffmanBracket.Planar.evaluateBracketStateSum trefoil KauffmanBracket.Planar.standardA
let jones = KauffmanBracket.Planar.jonesPolynomial trefoil KauffmanBracket.Planar.standardA

// Resource model for magic state distillation (p_out ≈ 35 p³ per round)
let estimate = MagicStateDistillation.estimateResources 0.9999 0.95
```

### Layer 5: Compilation & Integration

**Purpose:** Convert between gate-based and topological representations, optimize circuits, and enable standard algorithms on topological backends.

**Key Modules:**
- `GateToBraid.fs` - Convert gate-based circuits to braid sequences (22 unitary gate types)
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
open FSharp.Azure.Quantum.Algorithms
open FSharp.Azure.Quantum.GroverSearch

// Standard algorithms work on topological backends (8 qubits = 18 Ising anyons)
let backend20 = TopologicalUnifiedBackendFactory.createIsing 20
let result = AlgorithmExtensions.searchSingleWithTopology 42 8 backend20 Grover.defaultConfig
let qft = AlgorithmExtensions.qftWithTopology 4 backend20 QFT.defaultConfig
```

Running on the topological simulator models braiding; it is not faster than the gate-based simulator.

### Layer 6: Builders, Formats & Utilities

**Purpose:** Provide user-friendly DSL for composing quantum programs, file formats, and supporting utilities.

**Key Modules:**
- `TopologicalBuilder.fs` - Computation expression builder (`topological backend { ... }`)
- `TopologicalFormat.fs` - `.tqp` file format for serializing topological programs
- `NoiseModels.fs` - Configurable noise simulation for realistic error modeling
- `DeviceProfile.fs` - Descriptive parameters of Majorana (tetron) hardware generations, used by the noise presets
- `Visualization.fs` - State visualization and debugging utilities
- `TopologicalHelpers.fs` - Complex number utilities and particle display formatting
- `TopologicalError.fs` - Error types, TopologicalResult, and result computation expression

**Design Principles:**
- Familiar F# syntax (computation expressions)
- Type-safe composition
- State threaded through the builder context automatically
- Clear error messages

**Example:**
```fsharp
let program = topological backend {
    do! TopologicalBuilder.initialize AnyonSpecies.AnyonType.Ising 2
    do! TopologicalBuilder.braid 0
    do! TopologicalBuilder.braid 2
    let! outcome = TopologicalBuilder.measure 0
    return outcome
}

// Run it: Task<Result<AnyonSpecies.Particle, QuantumError>>
let outcomeTask = TopologicalBuilder.execute backend program
```

## Relationship with Gate-Based Library

### Different Paradigms, Shared Interface

**Fundamental Differences:**

| Aspect | Gate-Based | Topological |
|--------|-----------|-------------|
| **Operations** | H, CNOT, Rz gates | Braiding anyons |
| **State** | Amplitude vectors | Fusion trees |
| **Qubits** | Direct 2-state | Encoded in anyon pairs |
| **Measurement** | Z-basis (0 or 1) | Fusion outcomes (e.g. 1 or ψ for a σ pair) |
| **Algorithms** | Shor's, HHL, VQE | Kauffman, topological codes |

### Integration via IQuantumBackend

While the paradigms differ, the topological backend implements `IQuantumBackend` from the gate-based library. This enables:
- Standard algorithms (Grover, QFT, Shor, HHL) to run on topological backends
- Automatic gate-to-braid compilation (transparent to the caller)
- Backend-agnostic algorithm implementation

### Shared Patterns (Same Structure, Different Content)

The gate-based library groups files in folders; the topological library keeps its files in one folder, ordered by layer in the `.fsproj`:

```
Gate-Based (folders):               Topological (layers):
├── LocalSimulator/                 ├── Layer 1: AnyonSpecies.fs,
│   ├── Gates.fs                    │            FusionRules.fs, ...
│   └── StateVector.fs              │
├── Core/                           ├── Layer 2: TopologicalBackend.fs
│   └── BackendAbstraction.fs       │   (implements IQuantumBackend - shared!)
│       (IQuantumBackend)           │
├── Backends/                       │
│   └── LocalBackend.fs             │
├── Algorithms/                     ├── Layer 4/5: MagicStateDistillation.fs,
│   ├── Shor.fs                     │              AlgorithmExtensions.fs
│   └── Grover.fs                   │
└── Builders/                       └── Layer 6: TopologicalBuilder.fs
    └── CircuitBuilder.fs
```

## Design Principles

### 1. Idiomatic F#

✅ **Immutability by default**
```fsharp
// ✅ GOOD: Immutable state transformation - braiding returns a new superposition
let braidOnce (state: TopologicalOperations.Superposition) =
    TopologicalOperations.braidSuperposition 0 state

// ❌ BAD: a mutating method such as state.Braid(0) - the library has none
```

✅ **Pattern matching over inheritance**
```fsharp
// ✅ GOOD: Discriminated union (AnyonSpecies.Particle) and pattern matching
let describe (particle: AnyonSpecies.Particle) =
    match particle with
    | AnyonSpecies.Particle.Vacuum -> "vacuum"
    | AnyonSpecies.Particle.Sigma -> "Ising sigma"
    | AnyonSpecies.Particle.Psi -> "Ising fermion"
    | AnyonSpecies.Particle.Tau -> "Fibonacci tau"
    | AnyonSpecies.Particle.SpinJ(jDoubled, level) -> $"SU(2)_{level} spin {jDoubled}/2"

// ❌ BAD: a class hierarchy such as `type Sigma() = inherit Particle()`
```

✅ **Functions over methods**
```fsharp
// ✅ GOOD: Module with functions (FusionRules.channels a b theory)
let sigmaChannels =
    FusionRules.channels AnyonSpecies.Particle.Sigma AnyonSpecies.Particle.Sigma AnyonSpecies.AnyonType.Ising

// ❌ BAD: a static class method such as FusionRules.Channels(a, b, theory)
```

✅ **Composition over configuration**
```fsharp
// ✅ GOOD: Compose small functions (they return Result, so bind them)
let braidAt index = TopologicalOperations.braidSuperposition index
let braidWord = braidAt 0 >> Result.bind (braidAt 2) >> Result.bind (braidAt 0)

// ❌ BAD: a configuration object with Braid1/Braid2 properties
```

### 2. Type Safety

✅ **Domain-driven types**
```fsharp
// Not just strings! The library defines
//   AnyonSpecies.AnyonType = Ising | Fibonacci | SU2Level of level: int
//   AnyonSpecies.Particle  = Vacuum | Sigma | Psi | Tau | SpinJ of j_doubled: int * level: int
let theory = AnyonSpecies.AnyonType.SU2Level 3
```

✅ **Option for optional values**
```fsharp
// TopologicalOperations.OperationResult =
//   { State: FusionTree.State; Amplitude: Complex; ClassicalOutcome: AnyonSpecies.Particle option }
let outcomeName (result: TopologicalOperations.OperationResult) =
    match result.ClassicalOutcome with
    | Some particle -> string particle   // Not null!
    | None -> "no measurement"
```

✅ **Result for errors**
```fsharp
open FSharp.Azure.Quantum.Core.BackendAbstraction

let validateCapabilities (backend: IQuantumBackend) : Result<unit, string> =
    if backend.SupportsOperation(QuantumOperation.Braid 0) then Ok ()
    else Error "Backend lacks braiding support"
```

### 3. Testability

✅ **Business-meaningful assertions**
```fsharp
// In the test project this function carries xUnit's [<Fact>] attribute
let ``Two sigma anyons have two fusion channels (1 qubit)`` () =
    // Not just: Assert.Equal(2, dimension)
    // But: Clear business meaning!
    let sigma = AnyonSpecies.Particle.Sigma
    match FusionRules.channels sigma sigma AnyonSpecies.AnyonType.Ising with
    | Ok channels when List.length channels = 2 -> ()
    | other -> failwithf "Expected two fusion channels, got %A" other
```

✅ **Pure functions are inherently testable**
```fsharp
// No mocking needed - pure function!
let fused = FusionRules.channels AnyonSpecies.Particle.Sigma AnyonSpecies.Particle.Sigma AnyonSpecies.AnyonType.Ising
// fused = Ok [Vacuum; Psi]
```

### 4. Performance

✅ **Count before you enumerate**
```fsharp
let sigmas = List.replicate 6 AnyonSpecies.Particle.Sigma

// Counts the fusion space without building every tree
let dimension = FusionTree.fusionSpaceDimension sigmas AnyonSpecies.Particle.Vacuum AnyonSpecies.AnyonType.Ising

// Builds every tree - only when you need them
let allTrees = FusionTree.allTrees sigmas AnyonSpecies.Particle.Vacuum AnyonSpecies.AnyonType.Ising
```

✅ **Folds (or tail recursion) for long sequences**
```fsharp
let braidSequence (indices: int list) (state: TopologicalOperations.Superposition) =
    indices
    |> List.fold (fun acc index -> acc |> Result.bind (TopologicalOperations.braidSuperposition index)) (Ok state)
```

## Future Extensions

### Planned Layers

1. **Hardware Adapters** (Layer 2 Extensions)
   - Microsoft Majorana Gen 1 backend
   - Other topological hardware experiments

2. **Performance Optimizations**
   - GPU acceleration
   - Sparse matrices
   - Parallel braiding

### Integration Points

**Current Bridges:**
- Gate-to-braid compilation (GateToBraid, 22 unitary gate types including Fibonacci CZ/SWAP)
- Standard algorithm integration (AlgorithmExtensions - Grover, QFT, Shor, HHL, Fibonacci Grover)
- Braid-to-gate conversion with aggressive optimization (commutation cancellation, template matching)
- Toric code MWPM decoder (syndrome decoding with greedy matching)
- Surface code variants (planar code with boundary matching, color code on 4.8.8 lattice with greedy decoder)
- Anyonic error correction (charge violation detection, syndrome extraction, greedy charge-correction decoder)
- SU(2)_k F-matrix bridge (delegates to FMatrix.computeFMatrix/getFSymbol for general levels)
- Multi-term superposition measurement (Born-rule aggregation in TopologicalBuilder)
- Entanglement entropy (von Neumann entropy via density matrix and partial trace)
- Kauffman bracket knot invariants
- Pentagon/Hexagon equation verification
- Shared IQuantumBackend interface

**Potential Future Bridges:**
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
