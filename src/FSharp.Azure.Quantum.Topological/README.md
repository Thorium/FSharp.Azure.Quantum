# FSharp.Azure.Quantum.Topological

**Topological Quantum Computing Library for F#**

A topological quantum computing library for F#, implementing anyon models, fusion rules, braiding operators, and gate-to-braid compilation. While topological quantum computing is a fundamentally different paradigm -- information is encoded in the topology of anyon worldlines rather than in quantum amplitudes -- this library integrates seamlessly with the gate-based library (`FSharp.Azure.Quantum`) via the shared `IQuantumBackend` interface, enabling standard algorithms (Grover, QFT, Shor, HHL) to run on topological backends.

## Features

### Mathematical Foundation (Layer 1)
- **Anyon Species**: Ising (Majorana), Fibonacci, and SU(2)_k particle types with quantum dimensions
- **Fusion Rules**: Non-abelian fusion algebra (e.g., sigma x sigma = 1 + psi)
- **Braiding Operators**: R-matrices (braiding phases) and F-matrices (fusion basis changes)
- **Modular Data**: S-matrix, T-matrix, topological central charge
- **Knot Invariants**: Kauffman bracket and Jones polynomial via `KauffmanBracket`
- **Consistency Verification**: Pentagon and hexagon equation checks

### Backends and Operations (Layers 2-3)
- **TopologicalUnifiedBackend**: Implements `IQuantumBackend` for seamless integration with gate-based algorithms
- **TopologicalUnifiedBackendFactory**: Factory functions (`createIsing`, `createFibonacci`, `create`)
- **Fusion Trees**: Quantum state representation as recursive tree structures
- **TopologicalOperations**: Braiding, fusion measurement, superposition management

### Algorithms and Compilation (Layers 4-5)
- **Magic State Distillation**: 15-to-1 protocol for Ising anyon universality
- **Toric Code**: Topological error correction with syndrome detection
- **Gate-to-Braid Compilation**: Translate gate-based circuits to braid sequences (21 gate types)
- **Braid-to-Gate**: Convert braid sequences back to gate operations
- **Solovay-Kitaev**: Gate approximation for efficient braid decomposition
- **Algorithm Extensions**: Run Grover, QFT, Shor, and HHL on topological backends via `IQuantumBackend`
- **Knot Invariants**: Kauffman bracket, Jones polynomial, and standard knot constructors (trefoil, figure-eight, Hopf link, etc.)

### Developer Experience (Layer 6)
- **Computation Expressions**: `topological backend { ... }` builder for composing programs
- **TopologicalFormat**: Import/export `.tqp` files (human-readable format)
- **Noise Models**: Configurable noise simulation for realistic error modelling
- **Visualization**: State visualization and debugging utilities
- **TopologicalHelpers**: Complex number utilities and display formatting

## Installation

```bash
# Build the library
dotnet build src/FSharp.Azure.Quantum.Topological/FSharp.Azure.Quantum.Topological.fsproj

# Run tests
dotnet test tests/FSharp.Azure.Quantum.Topological.Tests/FSharp.Azure.Quantum.Topological.Tests.fsproj
```

## Quick Start

### Low-level API: Fusion and braiding primitives

```fsharp
open FSharp.Azure.Quantum.Topological

// Define Ising anyons
let sigma = AnyonSpecies.Particle.Sigma
let ising = AnyonSpecies.AnyonType.Ising

// Fuse two sigma anyons (non-abelian!)
let outcomes = FusionRules.fuse sigma sigma ising
// Result: [Vacuum; Psi] - two possible outcomes encode a qubit

// Get braiding phase
let R = BraidingOperators.element sigma sigma AnyonSpecies.Particle.Vacuum ising
// Result: e^(i*pi/8) - topological phase from braiding

// Check quantum dimension
let d = AnyonSpecies.quantumDimension sigma
// Result: sqrt(2) ~ 1.414
```

### Computation expression: Backend-agnostic programs

```fsharp
open FSharp.Azure.Quantum.Topological

let backend = TopologicalUnifiedBackendFactory.createIsing 10

let program = topological backend {
    let! state = initialize AnyonSpecies.AnyonType.Ising 4  // Create 4 sigma anyons
    do! braid 0                                              // Braid anyons 0 and 1
    do! braid 2                                              // Braid anyons 2 and 3
    let! outcome = measure 0                                 // Measure fusion of pair 0
    return outcome
}
```

### Running gate-based algorithms on topological backends

```fsharp
open FSharp.Azure.Quantum.Topological

// The topological backend implements IQuantumBackend, so standard algorithms work directly
let backend = TopologicalUnifiedBackendFactory.createIsing 20

// Grover search on topological backend (gate-to-braid compilation happens automatically)
let groverResult = AlgorithmExtensions.searchSingleWithTopology 42 8 backend config

// QFT on topological backend
let qftResult = AlgorithmExtensions.qftWithTopology 4 backend qftConfig

// Shor's factoring on topological backend
let shorResult = AlgorithmExtensions.factor15WithTopology backend
```

### Railway-oriented composition

```fsharp
let backend = TopologicalUnifiedBackendFactory.createIsing 10

// Sequential operations using Result.bind
match backend.InitializeState 2 with
| Ok state ->
    match backend.ApplyOperation (QuantumOperation.Braid 0) state with
    | Ok braided ->
        match backend.ApplyOperation (QuantumOperation.Measure 0) braided with
        | Ok measured -> printfn "Measured: %A" measured
        | Error e -> printfn "Measure error: %s" e.Message
    | Error e -> printfn "Braid error: %s" e.Message
| Error e -> printfn "Init error: %s" e.Message
```

## Test Coverage

**807 unit tests** covering all 29 modules across 6 architectural layers:

```bash
dotnet test tests/FSharp.Azure.Quantum.Topological.Tests/
```

Tests validate mathematical consistency (Pentagon/Hexagon equations, unitarity, fusion axioms), backend operations, computation expressions, format parsing, knot invariants, magic state distillation, and more.

## Architecture

The library follows a strictly layered architecture that mirrors the gate-based library's structure, integrating via the shared `IQuantumBackend` interface:

```
Layer 6: Builders & Formats      TopologicalBuilder, TopologicalFormat, Visualization,
                                 TopologicalHelpers
Layer 5: Compilation              GateToBraid, BraidToGate, SolovayKitaev, CircuitOptimization,
                                 AlgorithmExtensions
Layer 4: Algorithms               MagicStateDistillation, ToricCode, ErrorPropagation
Layer 3: Operations               TopologicalOperations, FusionTree
Layer 2: Backends                 TopologicalUnifiedBackend, TopologicalUnifiedBackendFactory
Layer 1: Mathematical Foundation  AnyonSpecies, FusionRules, BraidingOperators, FMatrix,
                                 RMatrix, ModularData, BraidGroup, BraidingConsistency,
                                 EntanglementEntropy, KauffmanBracket, KnotConstructors
```

### Why a Separate Package?

| Gate-Based (FSharp.Azure.Quantum) | Topological (This Library) |
|-----------------------------------|----------------------------|
| Qubits, gates, circuits | Anyons, braiding, fusion |
| Amplitude vectors | Fusion trees |
| Z-basis measurement | Fusion outcome measurement |
| Error-prone (needs QEC) | Topologically protected |
| Azure Quantum integration | Simulator + IQuantumBackend integration |

**Note:** While the paradigms differ, the topological backend implements `IQuantumBackend` from the gate-based library, enabling standard algorithms (Grover, QFT, Shor, HHL) to run on topological backends via automatic gate-to-braid compilation.

### Namespace Structure

```
FSharp.Azure.Quantum.Topological
  Layer 1: AnyonSpecies, FusionRules, BraidingOperators, FMatrix, RMatrix,
           ModularData, BraidGroup, BraidingConsistency, EntanglementEntropy,
           KauffmanBracket, KnotConstructors
  Layer 2: TopologicalUnifiedBackend, TopologicalUnifiedBackendFactory
  Layer 3: FusionTree, TopologicalOperations
  Layer 4: MagicStateDistillation, ToricCode, ErrorPropagation
  Layer 5: GateToBraid, BraidToGate, SolovayKitaev, CircuitOptimization,
           AlgorithmExtensions
  Layer 6: TopologicalBuilder, TopologicalBuilderExtensions, TopologicalFormat,
           NoiseModels, Visualization, TopologicalHelpers, TopologicalError
```

## Examples

Working examples are in [`examples/Topological/`](../../examples/Topological/):

| Example | Description |
|---------|-------------|
| `BasicFusion.fsx` | Fusion rules and anyon properties |
| `BellState.fsx` | Topological Bell state preparation |
| `BackendComparison.fsx` | Compare simulator backends |
| `FormatDemo.fsx` | `.tqp` format import/export |
| `MagicStateDistillation.fsx` | T-gate via 15-to-1 distillation |
| `ModularDataExample.fsx` | S/T matrices and modular invariants |
| `KauffmanJones.fsx` | Knot invariants from braiding |
| `TopologicalExample.fsx` | General topological operations |
| `TopologicalVisualization.fsx` | State visualization |
| `ToricCodeExample.fsx` | Toric code error correction |
| `bell-state.tqp` | Sample `.tqp` program file |

## Documentation

- **[Architecture Guide](../../docs/topological/architecture.md)** -- Layered design, module dependencies, design principles
- **[Developer Deep Dive](../../docs/topological/developer-deep-dive.md)** -- Comprehensive guide: paradigm shift, anyons, braiding, practical F# patterns
- **[Universal Quantum Computation](../../docs/topological/universal-quantum-computation.md)** -- Magic state distillation for Ising anyon universality
- **[Format Specification](../../docs/topological-format-spec.md)** -- `.tqp` file format reference

## Background: Topological Quantum Computing

### What are Anyons?

Anyons are quasiparticles in 2D systems with exotic exchange statistics -- neither bosonic nor fermionic. When you braid anyons around each other, the quantum state accumulates a topological phase that depends only on the braid pattern, not the specific path. This topological protection makes the stored quantum information exponentially resistant to local noise.

### Implemented Anyon Theories

1. **Ising Anyons (SU(2)_2)** -- Microsoft's Majorana zero mode approach. Particles: {1, sigma, psi}. Supports Clifford gates natively; needs magic state distillation for universality. Physically realizable.

2. **Fibonacci Anyons** -- Universal for quantum computation via braiding alone. Particles: {1, tau}. Golden ratio phi appears throughout. Not yet physically realized.

3. **SU(2)_k (General)** -- Framework for arbitrary Chern-Simons levels. k=2 (Ising) and k=3 are tested.

## Future Work

- **Azure Quantum Majorana**: Hardware backend integration (when available)
- **Surface Code Variants**: Planar codes, color codes
- **Performance**: GPU acceleration, sparse matrices, parallel braiding
- **Advanced Noise Models**: Thermal excitation, braiding imprecision beyond current NoiseModels

## References

1. **Topological Quantum** by Steven H. Simon (2023) -- Chapters 8-11
2. **Anyons in an exactly solved model and beyond** -- Kitaev (2006)
3. **Non-Abelian Anyons and Topological Quantum Computation** -- Nayak et al. (2008)
4. **Microsoft Quantum Documentation** -- Majorana-based quantum computing

## License

Same as parent project (FSharp.Azure.Quantum).
