# Topological Quantum Computing Documentation

> **Topological quantum computing** encodes quantum information in global topological properties of anyon worldlines, which in theory protects it against local errors. This library simulates that model on a classical computer.

## Documentation Overview

This directory contains comprehensive documentation for the **FSharp.Azure.Quantum.Topological** library, an idiomatic F# implementation of topological quantum computing concepts.

---

## Getting Started

**New to topological quantum computing?** Start here:

1. **[Getting Started Guide](./getting-started.md)** - Start here!  
   Install, build, and run your first topological computation in 5 minutes.

2. **[Architecture Guide](./architecture.md)**  
   Understand the library's layered architecture, from mathematical foundations to high-level algorithms.
   
3. **[Developer Deep Dive](./developer-deep-dive.md)**  
   Comprehensive guide for F# developers: paradigm shift, practical F# patterns, anyons, braiding, and advanced topics.

4. **[Universal Quantum Computation](./universal-quantum-computation.md)**  
   Learn how to achieve universal quantum computation using Ising anyons and magic state distillation.

---

## Documentation Files

### [getting-started.md](./getting-started.md)
**Purpose:** Quick-start guide to install, build, and run your first topological computation  
**Audience:** Any developer new to this library  
**Reading Time:** 5 minutes

**Contents:**
- Prerequisites and build instructions
- Three "first computation" options (computation expression, backend API, pure math)
- Key concepts mapped to library types
- Running the 10 built-in example scripts
- Error handling overview (railway-oriented `TopologicalResult<'T>` and the backend's `QuantumError`)

**Key Takeaway:** You can run a topological quantum computation in under 10 lines of F# using the `topological backend { }` computation expression builder.

---

### [architecture.md](./architecture.md)
**Purpose:** Library architecture and design principles  
**Audience:** Software architects, library contributors  
**Reading Time:** 15 minutes

**Contents:**
- Layered architecture overview (6 layers)
- Separation of concerns: Core → Operations → Backends → Algorithms
- Comparison with gate-based quantum computing library
- Module dependencies and compilation order
- Design patterns and best practices

**Key Takeaway:** Topological library follows same architectural principles as gate-based library, implementing a fundamentally different computational paradigm while sharing the `IQuantumBackend` interface for algorithm integration.

---

### [developer-deep-dive.md](./developer-deep-dive.md)
**Purpose:** In-depth technical guide for F# developers  
**Audience:** Senior F# engineers, quantum algorithm developers  
**Reading Time:** 30-45 minutes (initial read), reference thereafter

**Contents:**
- **Section 1:** Paradigm shift from matrices to topology
- **Section 2:** Library architecture and practical F# patterns
- **Section 3:** Anyons - particles with memory (Ising, Fibonacci)
- **Section 4:** Braiding operations as quantum gates (geometry, not matrices)
- **Section 5:** Advanced topics and production readiness

**Key Takeaway:** Topological QC stores information in *how* particles are braided in spacetime rather than in local degrees of freedom, which in theory suppresses local errors exponentially.

**Code Examples:**
- Creating anyons and fusion trees
- Performing braiding operations
- Implementing quantum algorithms
- Error handling with Result types
- Backend integration patterns

---

### [universal-quantum-computation.md](./universal-quantum-computation.md)
**Purpose:** Achieving universal quantum computation with Ising anyons  
**Audience:** Algorithm developers, researchers  
**Reading Time:** 20 minutes

**Contents:**
- The challenge: Ising anyons only support Clifford operations
- Solution: Magic state distillation for T-gates
- 15-to-1 distillation protocol (Bravyi-Kitaev 2005)
- Resource estimation and overhead analysis
- Complete worked examples
- API reference for MagicStateDistillation module

**Key Takeaway:** Clifford operations (native braiding) + T-gates (magic state injection) = Universal quantum computation

**Model Characteristics** (from the standard formula p_out ≈ 35 p³ that the module uses):
- Cubic error suppression per distillation round
- 5% error → 0.44% error with single round (11.4× improvement)
- Resource overhead: 15 noisy states per round, 15² = 225 for two rounds (enough for 99.99% fidelity from 5% input)

---

## Quick Navigation

### By Learning Path

**Beginner** (First time learning topological QC):
1. [Getting Started Guide](./getting-started.md) - Install, build, run your first computation
2. [Architecture Guide](./architecture.md) - Get the big picture
3. [Developer Deep Dive - Four Ideas](./developer-deep-dive.md#topological-quantum-computing-in-four-ideas) - Core concepts

**Intermediate** (Know basic topological QC concepts):
1. [Developer Deep Dive - Library Patterns](./developer-deep-dive.md#library-architecture-and-practical-patterns) - F# patterns and usage
2. [Developer Deep Dive - Braiding](./developer-deep-dive.md#braiding-operations---quantum-gates-as-geometry) - Braiding operations
3. [Universal Quantum Computation](./universal-quantum-computation.md) - Magic state distillation

**Advanced** (Building algorithms or contributing):
1. [Developer Deep Dive - Advanced Topics](./developer-deep-dive.md#advanced-topics-and-production-readiness) - Production patterns
2. [Universal Quantum Computation - Resource Estimation](./universal-quantum-computation.md#resource-estimation)
3. [Architecture Guide](./architecture.md) - Full layered design

---

### By Role

**Software Engineer** (Implementing features):
- [Getting Started Guide](./getting-started.md) → Quick setup and first computation
- [Architecture Guide](./architecture.md) → Understand module structure
- [Developer Deep Dive - Library Patterns](./developer-deep-dive.md#library-architecture-and-practical-patterns) → F# patterns and usage
- [Universal Quantum Computation - API Reference](./universal-quantum-computation.md#api-reference) → Function signatures

**Algorithm Developer** (Writing quantum algorithms):
- [Developer Deep Dive - Anyons](./developer-deep-dive.md#anyons---the-particles-with-memory) → Anyon theory
- [Developer Deep Dive - Braiding](./developer-deep-dive.md#braiding-operations---quantum-gates-as-geometry) → Braiding operations
- [Universal Quantum Computation](./universal-quantum-computation.md) → T-gate implementation
- [Architecture Guide - Layer 4](./architecture.md) → Algorithm patterns

**Researcher** (Exploring topological QC):
- [Developer Deep Dive - Paradigm Shift](./developer-deep-dive.md#the-paradigm-shift---from-matrices-to-topology) → Why topology matters
- [Universal Quantum Computation - Theory](./universal-quantum-computation.md#how-magic-state-distillation-works)
- [Developer Deep Dive - Advanced Topics](./developer-deep-dive.md#advanced-topics-and-production-readiness) → Error correction and production readiness

---

## API Quick Reference

### Core Modules

All modules live in the `FSharp.Azure.Quantum.Topological` namespace:

```
// Layer 1: Mathematical Foundation
AnyonSpecies      // Particle types and anyon theories
FusionRules       // Fusion algebra (sigma x sigma = 1+psi)
BraidingOperators // R-matrices and F-matrices
KauffmanBracket   // Knot invariants (Kauffman bracket, Jones polynomial)
KnotConstructors  // Standard knot/link diagram constructors (trefoil, figure-eight, Hopf link, etc.)

// Layer 2: Backends
TopologicalUnifiedBackendFactory  // Factory: createIsing, createFibonacci, createUnified, create
                                  // Returns IQuantumBackend for algorithm integration

// Layer 3: Operations
FusionTree        // Quantum state representation
TopologicalOperations // Braiding, measurement, superposition

// Layer 4: Algorithms
MagicStateDistillation // T-gate implementation via magic states

// Layer 5: Compilation & Integration
GateToBraid       // Convert gate-based circuits to braids
AlgorithmExtensions // Run Grover, QFT, Shor, HHL on topological backends
```

### Common Operations

```fsharp
open System
open FSharp.Azure.Quantum.Algorithms
open FSharp.Azure.Quantum.GroverSearch
open FSharp.Azure.Quantum.Topological

// Create simulator (unified backend - implements IQuantumBackend).
// The argument caps the anyon count: Ising needs 2n + 2 anyons for n qubits.
let backend = TopologicalUnifiedBackendFactory.createIsing 10

// Run standard algorithms on topological backend
let groverResult = AlgorithmExtensions.searchSingleWithTopology 5 3 backend Grover.defaultConfig
let qftResult = AlgorithmExtensions.qftWithTopology 3 backend QFT.defaultConfig

// Factoring 15 uses 15 qubits, so it needs a larger anyon budget
let shorResult = AlgorithmExtensions.factor15WithTopology (TopologicalUnifiedBackendFactory.createIsing 40)

// Computation expression
let program = topological backend {
    do! TopologicalBuilder.initialize AnyonSpecies.AnyonType.Ising 2
    do! TopologicalBuilder.braid 0
    do! TopologicalBuilder.braid 2
    let! outcome = TopologicalBuilder.measure 0
    return outcome
}

// Pure mathematical exploration (no backend needed)
let sigma = AnyonSpecies.Particle.Sigma
let ising = AnyonSpecies.AnyonType.Ising
let channels = FusionRules.channels sigma sigma ising
let R = BraidingOperators.element sigma sigma AnyonSpecies.Particle.Vacuum ising

// Knot invariants — use the planar-diagram path (KnotConstructors + KauffmanBracket.Planar)
// for real invariants, e.g. ⟨trefoil⟩ = −A⁵ − A⁻³ + A⁻⁷
let trefoil = KnotConstructors.trefoil true
let jones = KauffmanBracket.Planar.jonesPolynomial trefoil KauffmanBracket.Planar.standardA

// Magic state distillation: exactly 15 noisy Ising magic states in, one purified state out
let random = Random(42)
let purified =
    topologicalResult {
        let! magicState = MagicStateDistillation.prepareNoisyMagicState 0.05 AnyonSpecies.AnyonType.Ising
        return! MagicStateDistillation.distill15to1 random (List.replicate 15 magicState)
    }
```

---

## Complete Module Reference

The topological library consists of 31 source files organized in 6 architectural layers. Below is the complete reference with brief descriptions.

### Layer 1: Mathematical Foundation (Core Anyonic Theory)

**Purpose:** Pure mathematical constructs defining topological quantum computation - fusion rules, braiding matrices, modular data, knot invariants.

| Module | Description |
|--------|-------------|
| `AnyonSpecies.fs` | Anyon particle types, quantum dimensions, and anyon theories (Ising, Fibonacci) |
| `FusionRules.fs` | Fusion algebra rules (e.g., sigma x sigma = 1+psi for Ising anyons) |
| `BraidingOperators.fs` | R-matrices (braiding phase) and F-matrices (basis transformations) |
| `FMatrix.fs` | F-matrix (F-symbol) calculation and lookup tables for fusion tree basis changes |
| `RMatrix.fs` | R-matrix calculations for braiding operations |
| `ModularData.fs` | Modular tensor category data (S-matrix, T-matrix, topological central charge) |
| `BraidGroup.fs` | Braid group representations and generators |
| `BraidingConsistency.fs` | Pentagon and hexagon consistency equations for F and R matrices |
| `EntanglementEntropy.fs` | Topological entanglement entropy calculations |
| `KauffmanBracket.fs` | Kauffman bracket invariant and Jones polynomial for knot theory |
| `KnotConstructors.fs` | Standard knot/link diagram constructors (unknot, trefoil, figure-eight, Hopf link, etc.) |

### Layer 2: Backends (Execution Abstractions)

**Purpose:** Backend interfaces for executing topological operations — unified backend integrating with gate-based algorithms.

| Module | Description |
|--------|-------------|
| `TopologicalBackend.fs` | `TopologicalUnifiedBackend` implementing `IQuantumBackend` + `TopologicalUnifiedBackendFactory` |

### Layer 3: State Representation & Operations

**Purpose:** High-level operations on quantum states encoded as fusion trees.

| Module | Description |
|--------|-------------|
| `FusionTree.fs` | Quantum state representation as fusion trees of anyons |
| `TopologicalOperations.fs` | High-level operations: braiding, fusion measurement, superposition |

### Layer 4: Algorithms & Error Handling

**Purpose:** Quantum algorithms and error correction protocols built on topological primitives.

| Module | Description |
|--------|-------------|
| `MagicStateDistillation.fs` | T-gate synthesis via 15-to-1 distillation (Bravyi-Kitaev 2005) |
| `ToricCode.fs` | Topological error correction using toric code with MWPM decoder |
| `SurfaceCode.fs` | Surface code variants: planar code and color code (4.8.8 lattice) |
| `AnyonicErrorCorrection.fs` | Fusion-tree-level error correction: charge violation detection, syndrome extraction, greedy decoder, code space projection |
| `ErrorPropagation.fs` | Error propagation analysis through topological circuits |

### Layer 5: Compilation & Optimization

**Purpose:** Converting between gate-based and topological representations, circuit optimization, and running standard algorithms on topological backends.

| Module | Description |
|--------|-------------|
| `GateToBraid.fs` | Convert gate-based circuits to braid sequences (22 unitary gate types, plus Measure and Barrier; Reset and classically conditioned gates are rejected) |
| `BraidToGate.fs` | Convert braid sequences back to gate operations |
| `SolovayKitaev.fs` | Gate approximation algorithm for efficient decomposition |
| `CircuitOptimization.fs` | Circuit optimization and simplification strategies |
| `AlgorithmExtensions.fs` | Run Grover, QFT, Shor, and HHL algorithms on topological backends |

### Layer 6: Builders, Formats & Utilities

**Purpose:** Developer-friendly APIs, file formats, and supporting utilities.

| Module | Description |
|--------|-------------|
| `TopologicalBuilder.fs` | F# computation expressions for building topological circuits |
| `TopologicalFormat.fs` | `.tqp` file format for serializing topological programs |
| `DeviceProfile.fs` | Descriptive parameters of Majorana (tetron) hardware generations, used to derive noise presets |
| `NoiseModels.fs` | Noise simulation for realistic error modeling |
| `Visualization.fs` | State visualization and debugging utilities |
| `TopologicalError.fs` | `TopologicalError`, `TopologicalResult<'T>` and the `topologicalResult { }` builder |
| `TopologicalHelpers.fs` | Complex number utilities and display formatting for particles |

---

## Library Features

### Implemented

- **Ising Anyons** (Majorana zero modes)
- **Fibonacci Anyons** (SU(2) level k=3)
- **SU(2)_k Anyons** (general Chern-Simons levels with computational basis encoding)
- **Fusion Trees** (complete state representation)
- **Braiding Operations** (R-matrices, F-matrices, F-moves)
- **Measurement** (fusion outcome detection)
- **Magic State Distillation** (15-to-1 protocol)
- **Gate-to-Braid Compilation** (22 unitary gate types)
- **Braid-to-Gate Conversion** (reverse compilation with aggressive optimization)
- **Unified Backend** (IQuantumBackend implementation)
- **Algorithm Extensions** (Grover, QFT, Shor, HHL on topological backends, including Fibonacci-specific Grover search)
- **Knot Constructors** (standard knot/link diagrams)
- **Kauffman Bracket** (knot invariant evaluator with Jones polynomial)
- **Noise Models** (configurable error simulation)
- **Toric Code** (topological error correction with MWPM decoder)
- **Surface Code Variants** (planar code with boundary matching, color code on 4.8.8 lattice with greedy decoder)
- **Anyonic Error Correction** (fusion-tree-level charge violation detection, syndrome extraction, greedy charge-correction decoder, code space projection)
- **Pentagon/Hexagon Verification** (F-matrix and R-matrix consistency checks)
- **Entanglement Entropy** (von Neumann entropy, partial trace, density matrices)
- **Solovay-Kitaev Algorithm** (gate approximation over Clifford+T, and over Fibonacci braid generators)

### Planned (Future Development)

- **Hardware backends** (Azure Quantum Majorana integration)
- **Performance optimizations** (GPU acceleration, parallel braiding)

---

## External Resources

### Books
- **Steven Simon - "Topological Quantum"** (2023)  
  Comprehensive textbook for topological quantum computing. Chapters 1-7 cover foundations.

### Papers
- **Bravyi & Kitaev (2005)** - "Universal quantum computation with ideal Clifford gates and noisy ancillas"  
  Foundational paper on magic state distillation.

- **Nayak et al. (2008)** - "Non-Abelian anyons and topological quantum computation"  
  Comprehensive review article (Reviews of Modern Physics).

### Online Resources
- [Microsoft Quantum - Topological Quantum Computing](https://www.microsoft.com/en-us/research/project/topological-quantum-computing/)
- [Wikipedia - Topological Quantum Computer](https://en.wikipedia.org/wiki/Topological_quantum_computer)
- [arXiv:0707.1889](https://arxiv.org/abs/0707.1889) - "A Short Introduction to Topological Quantum Computation"

---

## Contributing

Found an issue or want to contribute? Please open an issue or submit a pull request on GitHub.

### Documentation Improvements

This documentation is a living resource. If you find:
- Unclear explanations
- Missing code examples
- Broken links
- Technical inaccuracies

Please open an issue or submit a PR!

---

## License

This documentation and the FSharp.Azure.Quantum.Topological library are released into the public domain; see [LICENSE](../../LICENSE).

---

## Contact & Support

- **Issues:** [GitHub Issues](https://github.com/Thorium/FSharp.Azure.Quantum/issues)
- **Discussions:** [GitHub Discussions](https://github.com/Thorium/FSharp.Azure.Quantum/discussions)
- **Repository:** [FSharp.Azure.Quantum](https://github.com/Thorium/FSharp.Azure.Quantum)

---

**Last Updated:** September 2026  
**Library Version:** 0.4.12  
**F# Version:** 10.0
