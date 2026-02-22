# Getting Started with FSharp.Azure.Quantum.Topological

A quick guide to install, build, and run your first topological quantum computation. Aimed at senior .NET and F# developers -- no quantum physics PhD required.

**Time to first working program**: ~5 minutes

---

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download) or later
- Basic F# knowledge (discriminated unions, Result types, computation expressions)
- No quantum computing experience needed (but see [`quantum-computing-introduction.md`](../quantum-computing-introduction.md) for context)

## Build and Test

```bash
# Clone the repository
git clone https://github.com/Thorium/FSharp.Azure.Quantum.git
cd FSharp.Azure.Quantum

# Build the topological library
dotnet build src/FSharp.Azure.Quantum.Topological/FSharp.Azure.Quantum.Topological.fsproj

# Run the test suite (~807 tests)
dotnet test tests/FSharp.Azure.Quantum.Topological.Tests/FSharp.Azure.Quantum.Topological.Tests.fsproj
```

## Your First Computation

### Option A: Using the computation expression builder

Create a file `MyFirstTopological.fsx`:

```fsharp
#r "src/FSharp.Azure.Quantum/bin/Debug/net10.0/FSharp.Azure.Quantum.dll"
#r "src/FSharp.Azure.Quantum.Topological/bin/Debug/net10.0/FSharp.Azure.Quantum.Topological.dll"

open FSharp.Azure.Quantum.Topological

// Create a backend -- this is a classical simulator for Ising anyons
let backend = TopologicalUnifiedBackendFactory.createIsing 10

// Write a topological program using the computation expression
let bellProgram = topological backend {
    // Initialize 4 sigma anyons (encodes 2 topological qubits)
    do! TopologicalBuilder.initialize AnyonSpecies.AnyonType.Ising 4

    // Braid anyons 0 and 1 (geometric operation, not matrix multiplication)
    do! TopologicalBuilder.braid 0

    // Braid anyons 2 and 3
    do! TopologicalBuilder.braid 2

    // Measure fusion of the first pair
    let! outcome = TopologicalBuilder.measure 0
    return outcome
}

// Execute and handle the result
let result =
    TopologicalBuilder.execute backend bellProgram
    |> Async.AwaitTask |> Async.RunSynchronously

match result with
| Ok outcome -> printfn "Fusion outcome: %A" outcome
| Error err  -> printfn "Error: %s" err.Message
```

> **Preferred async pattern:** Use `task { }` for non-blocking execution:
> ```fsharp
> task {
>     let! result = TopologicalBuilder.execute backend bellProgram
>     match result with
>     | Ok outcome -> printfn "Fusion outcome: %A" outcome
>     | Error err  -> printfn "Error: %s" err.Message
> }
> ```

Run it:
```bash
dotnet fsi MyFirstTopological.fsx
```

### Option B: Using the backend API directly

```fsharp
#r "src/FSharp.Azure.Quantum/bin/Debug/net10.0/FSharp.Azure.Quantum.dll"
#r "src/FSharp.Azure.Quantum.Topological/bin/Debug/net10.0/FSharp.Azure.Quantum.Topological.dll"

open FSharp.Azure.Quantum.Topological

// Create a unified backend (implements IQuantumBackend)
let backend = TopologicalUnifiedBackendFactory.createIsing 10

// Use the synchronous IQuantumBackend API
match backend.InitializeState 4 with
| Ok initialState ->
    // Apply braid operation (gate-to-braid compilation happens automatically)
    match backend.ApplyOperation (QuantumOperation.Braid 0) initialState with
    | Ok braidedState ->
        // Measure
        match backend.Measure braidedState 1 with
        | Ok measurements ->
            printfn "Measurement results: %A" measurements
        | Error e -> printfn "Measurement error: %A" e
    | Error e -> printfn "Braid error: %A" e
| Error e -> printfn "Init error: %A" e
```

### Option C: Pure mathematical exploration (no backend needed)

```fsharp
#r "src/FSharp.Azure.Quantum.Topological/bin/Debug/net10.0/FSharp.Azure.Quantum.Topological.dll"

open FSharp.Azure.Quantum.Topological

let ising = AnyonSpecies.AnyonType.Ising
let sigma = AnyonSpecies.Particle.Sigma

// What happens when two sigma anyons fuse?
let channels = FusionRules.channels sigma sigma ising
printfn "sigma x sigma = %A" channels
// Output: Ok [Vacuum; Psi] -- two possible outcomes, this is a qubit!

// What is the quantum dimension of a sigma anyon?
let d = AnyonSpecies.quantumDimension sigma
printfn "d_sigma = %.4f" d
// Output: 1.4142 (sqrt 2)

// What phase does braiding add?
let R = BraidingOperators.element sigma sigma AnyonSpecies.Particle.Vacuum ising
printfn "R^{sigma,sigma}_vacuum = %A" R
// Output: Ok (e^(i*pi/8))
```

## Key Concepts in 60 Seconds

| Concept | What it means | Library type |
|---------|---------------|-------------|
| **Anyon** | A quasiparticle in 2D with exotic exchange statistics | `AnyonSpecies.Particle` |
| **Fusion** | Combining two anyons -- the result is non-deterministic (this encodes a qubit) | `FusionRules.channels` |
| **Braiding** | Moving anyons around each other -- applies a topological phase (this is a gate) | `BraidingOperators.element` |
| **Fusion tree** | The data structure representing the quantum state | `FusionTree` |
| **Backend** | Executes topological operations (unified: `TopologicalUnifiedBackend` via `IQuantumBackend`) | `TopologicalUnifiedBackendFactory` |

**Why topological?** Information is stored in the topology of anyon worldlines, not in fragile quantum amplitudes. Local noise cannot change global topology, giving exponential error suppression.

## Run the Built-in Examples

The [`examples/Topological/`](../../examples/Topological/) directory has 10 runnable scripts:

```bash
# Start here -- basic fusion rules
dotnet fsi examples/Topological/BasicFusion.fsx

# Bell state via braiding
dotnet fsi examples/Topological/BellState.fsx

# Knot invariants
dotnet fsi examples/Topological/KauffmanJones.fsx

# Magic state distillation (T-gate for Ising universality)
dotnet fsi examples/Topological/MagicStateDistillation.fsx

# All examples accept CLI flags:
dotnet fsi examples/Topological/BasicFusion.fsx -- --example 3 --trials 500
dotnet fsi examples/Topological/BasicFusion.fsx -- --help
```

## Error Handling

The library uses railway-oriented programming -- all public APIs return `Result<'T, TopologicalError>`:

```fsharp
// Errors are explicit, composable, and never thrown as exceptions
type TopologicalError =
    | ValidationError of message: string
    | LogicError of message: string
    | ComputationError of message: string
    | BackendError of message: string
    | NotImplemented of message: string
```

Use `Result.bind` / `Result.map` for composition, or the `taskResult { }` computation expression for sequential error propagation.

## What to Read Next

1. **[Developer Deep Dive](./developer-deep-dive.md)** -- Full guide covering architecture, practical F# patterns, anyon theory, and braiding operations
2. **[Architecture Guide](./architecture.md)** -- Layered design, module dependencies, design principles
3. **[Universal Quantum Computation](./universal-quantum-computation.md)** -- How magic state distillation makes Ising anyons universal
4. **[Source README](../../src/FSharp.Azure.Quantum.Topological/README.md)** -- Module reference and complete feature list

## Coming from Gate-Based Quantum Computing?

If you already know Qiskit, Q#, or Cirq, here is the mapping:

| Gate-Based | Topological Equivalent | Library API |
|------------|----------------------|-------------|
| Qubit | Pair of sigma anyons | `backend.InitializeState 4` (4 anyons = 2 qubits) |
| Gate (H, CNOT) | Braid operation | `backend.ApplyOperation (QuantumOperation.Braid index) state` |
| Measurement | Fusion measurement | `backend.Measure state shots` |
| Circuit | Braid sequence | `topological backend { ... }` |
| State vector | Fusion tree superposition | `FusionTree` + `TopologicalOperations.Superposition` |
| Algorithm | Algorithm extension | `AlgorithmExtensions.searchSingleWithTopology` etc. |
