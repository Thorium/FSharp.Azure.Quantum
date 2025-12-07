# Topological Quantum Computing Examples

This directory contains examples demonstrating the **topological quantum computing simulator** from `FSharp.Azure.Quantum.Topological`.

## What is Topological Quantum Computing?

Topological quantum computing uses **anyons** (exotic particles in 2D systems) and **braiding operations** instead of traditional quantum gates. The key advantage is **topological protection**: quantum information is encoded in global topological properties that are immune to local noise.

**Microsoft's Approach:** Majorana zero modes (Ising anyons) in topological superconductors.

## Examples

### 1. BasicFusion.fsx - Fundamental Fusion Rules

**Demonstrates:**
- Initializing Ising anyons (σ particles)
- Fusion measurement (σ × σ = 1 + ψ)
- Statistical verification of fusion rules
- Building fusion trees with multiple anyons

**Run:**
```bash
dotnet fsi BasicFusion.fsx
```

**Key Concepts:**
- Ising anyons: `{1 (vacuum), σ (sigma), ψ (psi)}`
- Fusion creates quantum superposition
- Measurement collapses to classical outcome

### 2. BellState.fsx - Entanglement via Braiding

**Demonstrates:**
- Creating entangled states using braiding operations
- Topological equivalent of Bell state |Φ⁺⟩ = (|00⟩ + |11⟩) / √2
- Correlation measurements (verifying entanglement)
- Worldline braiding visualization

**Run:**
```bash
dotnet fsi BellState.fsx
```

**Key Concepts:**
- Braiding creates entanglement geometrically
- Correlated measurement outcomes
- Topological computation expression (`topological { ... }`)

### 3. BackendComparison.fsx - Ising vs Fibonacci Anyons

**Demonstrates:**
- Comparing backend capabilities
- Ising anyons (Microsoft Majorana) vs Fibonacci anyons (theoretical)
- Performance benchmarking
- Fusion rule differences
- Backend validation

**Run:**
```bash
dotnet fsi BackendComparison.fsx
```

**Key Concepts:**
- Ising: Clifford-only (requires magic states for universality)
- Fibonacci: Universal braiding (τ × τ = 1 + τ)
- Hardware status: Ising experimental, Fibonacci theoretical

### 4. FormatDemo.fsx - Import/Export .tqp Files

**Demonstrates:**
- Creating programs programmatically and saving to `.tqp` files
- Loading `.tqp` files and parsing them
- Round-trip serialization (program → file → program)
- Executing programs from `.tqp` files
- Working with different anyon types

**Run:**
```bash
dotnet fsi FormatDemo.fsx
```

**Key Concepts:**
- `.tqp` format: Human-readable topological quantum program format
- Similar to OpenQASM for gate-based QC
- Parser/Serializer modules for file I/O
- Example file: `bell-state.tqp` (commented Bell state program)

## Prerequisites

The examples use the local build of `FSharp.Azure.Quantum.Topological`. Make sure the library is built:

```bash
# From repository root
cd src/FSharp.Azure.Quantum.Topological
dotnet build
```

## Architecture

The topological simulator is **independent from gate-based quantum computing**:

```
Gate-Based QC          Topological QC
──────────────         ──────────────
IQuantumBackend    ←→  ITopologicalBackend
  ├─ LocalBackend        ├─ SimulatorBackend (Ising)
  ├─ IonQ                └─ SimulatorBackend (Fibonacci)
  └─ Quantinuum
    
Circuit Model          Braiding Model
  H, CNOT, T     ←→  Braid, FMove, Measure
  |0⟩, |1⟩       ←→  σ, τ (anyons)
  Amplitudes     ←→  Fusion channels
```

## Core API

### Creating a Backend

```fsharp
open FSharp.Azure.Quantum.Topological

// Ising anyons (Microsoft Majorana)
let backend = TopologicalBackend.createSimulator AnyonSpecies.AnyonType.Ising 10

// Fibonacci anyons (theoretical universal)
let backend = TopologicalBackend.createSimulator AnyonSpecies.AnyonType.Fibonacci 10
```

### Direct Backend Operations

```fsharp
// Initialize anyons
let! stateResult = backend.Initialize AnyonSpecies.AnyonType.Ising 4

match stateResult with
| Ok state ->
    // Braid operation
    let! braidedResult = backend.Braid 0 state
    
    match braidedResult with
    | Ok braidedState ->
        // Measure fusion
        let! measureResult = backend.MeasureFusion 0 braidedState
        
        match measureResult with
        | Ok (outcome, collapsedState, probability) ->
            printfn "Outcome: %A (p=%.2f)" outcome probability
        | Error err ->
            printfn "Measurement failed: %s" err.Message
    | Error err ->
        printfn "Braiding failed: %s" err.Message
| Error err ->
    printfn "Initialization failed: %s" err.Message
```

### Computation Expression (Recommended)

```fsharp
let! result = topological backend {
    // Initialize
    do! TopologicalBuilder.initialize AnyonSpecies.AnyonType.Ising 4
    
    // Braiding operations
    do! TopologicalBuilder.braid 0
    do! TopologicalBuilder.braid 2
    
    // Measurement
    let! (outcome, _) = TopologicalBuilder.measure 0
    
    return outcome
}

match result with
| Ok outcome -> printfn "Measured: %A" outcome
| Error err -> printfn "Error: %s" err.Message
```

## Comparison with Gate-Based Examples

| Gate-Based Example | Topological Equivalent |
|--------------------|------------------------|
| `CircuitBuilder/BellState.fsx` | `TopologicalSimulator/BellState.fsx` |
| H-CNOT circuit | Braiding operations |
| Qubit measurement | Fusion measurement |
| `IQuantumBackend` | `ITopologicalBackend` |

## Learning Resources

1. **Library Documentation:** `src/FSharp.Azure.Quantum.Topological/README.md`
2. **Format Specification:** `docs/topological-format-spec.md` - `.tqp` file format
3. **Test Suite:** `tests/FSharp.Azure.Quantum.Topological.Tests/` - 166 unit tests (14 format tests included)
4. **Research:** See `TKT-102-research-spec.md` for theoretical background

## FAQ

**Q: Why use topological QC instead of gate-based?**  
A: Topological protection provides inherent fault-tolerance (theoretical error rates ~10⁻¹² vs 10⁻³ for gate-based).

**Q: Can I run existing quantum circuits on topological backend?**  
A: Not directly. Topological QC uses a different computational model (braiding vs gates). Some circuits can be translated, but this library uses native topological operations.

**Q: Which anyon type should I use?**  
A: Use **Ising** for Microsoft hardware emulation, **Fibonacci** for theoretical research.

**Q: How does this relate to Microsoft's quantum computing efforts?**  
A: Microsoft is developing Majorana-based topological qubits. This simulator emulates that approach using Ising anyons.

**Q: Is this faster than gate-based simulators?**  
A: No - topological simulation has similar complexity (exponential in anyon count). The value is educational and hardware emulation, not performance.

## Contributing

Found a bug? Want to add more examples? See the main repository's contribution guidelines.

## License

Same as `FSharp.Azure.Quantum` - see repository root LICENSE file.
