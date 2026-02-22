---
layout: default
title: Switching Between Local and Azure Backends
---

# Switching Between Local and Azure Backends

**How to seamlessly switch between local simulation and Azure Quantum execution**

## Overview

FSharp.Azure.Quantum provides a **unified API** through the `QuantumBackend` module that works with both:

1. **Local Simulator** - Fast, free, offline simulation (up to 20 qubits)
2. **Azure Quantum** - Scalable cloud execution with real quantum hardware access (requires Azure subscription and workspace configuration)

**Key Feature:** The same `QaoaCircuit` type is used for both backends, making backend switching a **one-line code change**.

## The Unified API

### Current Implementation 

The library provides a unified backend abstraction that works with both local simulation and cloud quantum backends:

```fsharp
open FSharp.Azure.Quantum.Quantum.QuantumTspSolver
open FSharp.Azure.Quantum.Backends

// Define TSP problem (3 cities)
let distances = array2D [
    [ 0.0; 1.0; 2.0 ]
    [ 1.0; 0.0; 1.5 ]
    [ 2.0; 1.5; 0.0 ]
]

// Method 1: Local simulator backend (up to 20 qubits)
let localBackend = LocalBackendFactory.createUnified()
match solve localBackend distances defaultConfig with
| Ok solution -> printfn "Tour: %A, Length: %.2f" solution.Tour solution.TourLength
| Error err -> printfn "Error: %s" err.Message

// Note: Cloud backends (IonQ/Rigetti) require Azure Quantum workspace configuration
// and are created using workspace-specific factory methods (see Azure Quantum documentation)
```

**That's it!** Same solver API, different backends - just swap the backend creation.

## Simple Backend Switching

### Example: Switching with a Single Line

```fsharp
open FSharp.Azure.Quantum.Quantum.QuantumTspSolver
open FSharp.Azure.Quantum.Backends

/// Solve TSP problem with different backends
let solveTsp distances =
    // Define TSP problem (distance matrix already defined)
    
    // CHANGE THIS ONE LINE TO SWITCH BACKENDS:
    let backend = LocalBackendFactory.createUnified()  // ← Local simulation
    // Note: Cloud backends require Azure Quantum workspace configuration
    
    // Same solver API for all backends
    solve backend distances defaultConfig

// Use it
let distances2 = array2D [
    [ 0.0; 1.0; 2.0 ]
    [ 1.0; 0.0; 1.5 ]
    [ 2.0; 1.5; 0.0 ]
]

match solveTsp distances2 with
| Ok solution ->
    printfn "Best tour: %A" solution.Tour
    printfn "Tour length: %.2f" solution.TourLength
| Error err ->
    eprintfn "Error: %s" err.Message
```

### Uniform Result Format

All backends return the same `TspSolution` type (via `solve`):

```fsharp
type TspSolution = {
    /// Optimal tour (city visit order)
    Tour: int[]
    
    /// Total tour length (distance)
    TourLength: float
    
    /// Execution method ("Quantum" or "Classical")
    Method: string
    
    /// Explanation of why this method was chosen
    Reasoning: string
    
    /// Optimized QAOA parameters (γ, β) if quantum
    OptimizedParameters: float * float
    
    /// Whether optimization converged
    OptimizationConverged: bool
    
    /// Number of optimization iterations
    OptimizationIterations: int
}
```

This means:
- ✅ Analysis code works with any backend
- ✅ Logging and metrics are consistent
- ✅ Visualization tools are backend-agnostic
- ✅ Easy to compare local vs cloud results

## Configuration-Based Switching

For production applications, use configuration to control backend selection:

```fsharp
open FSharp.Azure.Quantum.Backends
open FSharp.Azure.Quantum.Quantum.QuantumTspSolver

module BackendConfig =
    
    type Config =
        | Local
        // Note: Cloud backends require workspace-specific configuration
    
    let getBackend (config: Config) =
        match config with
        | Local -> 
            LocalBackendFactory.createUnified()
    
    // Load config from environment
    let fromEnvironment () =
        match System.Environment.GetEnvironmentVariable("QUANTUM_BACKEND") with
        | _ -> Local  // Default: local simulator

// Usage
let config = BackendConfig.fromEnvironment ()
let backend = BackendConfig.getBackend config
match solve backend distances defaultConfig with
| Ok solution -> printfn "Solution: %A" solution
| Error err -> printfn "Error: %s" err.Message
```

Set backend via environment variable:

```bash
# Local execution (default)
export QUANTUM_BACKEND=local
dotnet run

# Cloud backends require Azure Quantum workspace configuration
```

## Unified High-Level API

**Current API** - Already provides unified solving:

```fsharp
open FSharp.Azure.Quantum.Quantum.QuantumTspSolver
open FSharp.Azure.Quantum.Backends

let backend = LocalBackendFactory.createUnified()  // Local simulation
match solve backend distances defaultConfig with
| Ok solution -> 
    printfn "Tour: %A" solution.Tour
    printfn "Length: %.2f" solution.TourLength
| Error err -> 
    eprintfn "Error: %s" err.Message
```

This API provides:
- ✅ Unified backend abstraction (local and cloud)
- ✅ Unified result format
- ✅ Error handling with Result type
- ✅ Easy backend switching (one-line change)

## Comparison: Local vs Azure

### When to Use Local Simulator

| Scenario | Local | Azure |
|----------|-------|-------|
| **Development** | ✅ Instant feedback | ❌ Network latency |
| **Unit Testing** | ✅ Fast, reliable | ❌ Slow, costs money |
| **Small problems** (≤20 qubits) | ✅ Free, fast | ❌ Overkill |
| **Offline work** | ✅ No internet needed | ❌ Requires connection |
| **Algorithm prototyping** | ✅ Rapid iteration | ❌ Slower iteration |

### When to Use Azure Quantum

| Scenario | Local | Azure |
|----------|-------|-------|
| **Large problems** (>20 qubits) | ❌ Not supported | ✅ Scales to 20+ qubits |
## Comparison: Local vs Azure

| Feature | Local Simulator | Azure Quantum |
|---------|----------------|---------------|
| **Qubit limit** | ≤20 qubits (1048576 dimensions) | 100+ qubits (cloud) |
| **Cost** | Free | Pay per shot |
| **Network** | Offline capable | Requires internet |
| **Speed (3 cities)** | <100ms | Seconds (network + queue) |
| **Use cases** | Development, testing, small problems | Production, large problems, research |
| **Hardware access** | ❌ Simulation only | ✅ IonQ, Rigetti, etc. |

## Qubit Limit Awareness with IQubitLimitedBackend

### The IQubitLimitedBackend Interface

Some backends have a maximum number of qubits they can handle. The `IQubitLimitedBackend` interface provides a **non-breaking, opt-in extension** to `IQuantumBackend` that lets backends advertise their capacity:

```fsharp
/// Optional interface for backends that have qubit limits.
/// Inherits from IQuantumBackend — existing backends are unaffected.
type IQubitLimitedBackend =
    inherit IQuantumBackend
    
    /// Maximum number of qubits this backend supports, or None if unlimited.
    abstract MaxQubits : int option
```

**Key design points:**
- ✅ **Non-breaking** — backends that don't implement it continue to work unchanged
- ✅ **Optional** — callers use a type-test pattern to check at runtime
- ✅ `LocalBackend` implements it with `MaxQubits = Some 20`

### Querying Backend Limits

Use standard F# pattern matching to check whether a backend reports a qubit limit:

```fsharp
open FSharp.Azure.Quantum.Backends

let backend = LocalBackendFactory.createUnified()

// Pattern match to discover qubit limits
let maxQubits =
    match backend with
    | :? IQubitLimitedBackend as lb -> lb.MaxQubits
    | _ -> None

match maxQubits with
| Some n -> printfn "Backend supports up to %d qubits" n
| None   -> printfn "Backend does not report a qubit limit"
```

### UnifiedBackend.getMaxQubits Helper

For convenience, `UnifiedBackend` exposes a helper that wraps the pattern-match logic above:

```fsharp
module UnifiedBackend =
    
    /// Returns the maximum qubit count for a backend, or None if the
    /// backend does not implement IQubitLimitedBackend.
    let getMaxQubits (backend: IQuantumBackend) : int option =
        match backend with
        | :? IQubitLimitedBackend as lb -> lb.MaxQubits
        | _ -> None
```

Usage is straightforward:

```fsharp
open FSharp.Azure.Quantum.Backends

let backend = LocalBackendFactory.createUnified()
let limit = UnifiedBackend.getMaxQubits backend
// limit = Some 20 for LocalBackend
```

### Integration with ProblemDecomposition

`ProblemDecomposition` uses `IQubitLimitedBackend` to **automatically decompose** problems that exceed the backend's qubit capacity. When a problem requires more qubits than the backend can handle, the decomposer splits it into smaller sub-problems, solves each independently, and merges the results:

```fsharp
open FSharp.Azure.Quantum.Backends
open FSharp.Azure.Quantum.Quantum.QuantumTspSolver

let backend = LocalBackendFactory.createUnified()  // MaxQubits = Some 20
let largeProblem = (* distance matrix for 10+ cities *)

// ProblemDecomposition checks the backend's qubit limit automatically.
// If the problem exceeds the limit, it decomposes into sub-problems,
// solves each within the backend's capacity, and merges the results.
match solve backend largeProblem defaultConfig with
| Ok solution ->
    printfn "Tour: %A" solution.Tour
    printfn "Length: %.2f" solution.TourLength
| Error err ->
    eprintfn "Error: %s" err.Message
```

The decomposition flow:

1. **Check capacity** — calls `UnifiedBackend.getMaxQubits` on the active backend
2. **Estimate qubit requirement** — computes qubits needed for the given problem size
3. **Decompose if needed** — splits the problem into chunks that each fit within `MaxQubits`
4. **Solve sub-problems** — runs QAOA on each chunk using the same backend
5. **Merge results** — combines sub-solutions into a single `TspSolution`

This is fully transparent to the caller — the same `solve` function handles both small problems (direct execution) and large problems (automatic decomposition).

## Async Backend Execution

All backends support **Task-based async execution** with `CancellationToken` support. This is especially important for cloud backends where network I/O introduces latency.

### IQuantumBackend Async Interface

```fsharp
type IQuantumBackend =
    // Sync (original)
    abstract member ExecuteToState: ICircuit -> Result<QuantumState, QuantumError>
    abstract member ApplyOperation: QuantumOperation -> QuantumState -> Result<QuantumState, QuantumError>
    
    // Async (new)
    abstract member ExecuteToStateAsync: ICircuit -> CancellationToken -> Task<Result<QuantumState, QuantumError>>
    abstract member ApplyOperationAsync: QuantumOperation -> QuantumState -> CancellationToken -> Task<Result<QuantumState, QuantumError>>
    
    // Other members...
    abstract member Name: string
    abstract member NativeStateType: QuantumStateType
    abstract member SupportsOperation: QuantumOperation -> bool
    abstract member InitializeState: int -> Result<QuantumState, QuantumError>
```

### Async Usage Example

```fsharp
open System.Threading
open System.Threading.Tasks
open FSharp.Azure.Quantum.Backends

// Use task { } computation expression for async workflows
let solveAsync (backend: IQuantumBackend) circuit (ct: CancellationToken) =
    task {
        let! result = backend.ExecuteToStateAsync circuit ct
        match result with
        | Ok state -> return Ok state
        | Error err -> return Error err
    }

// Run with cancellation support
let cts = new CancellationTokenSource(TimeSpan.FromSeconds(30.0))
let result = 
    solveAsync backend circuit cts.Token
    |> Async.AwaitTask |> Async.RunSynchronously
```

### Async with Cloud Backends

Cloud backends benefit most from async since they involve HTTP calls, job submission, and polling:

```fsharp
open System.Net.Http
open FSharp.Azure.Quantum.Backends

// Create cloud backend via factory
let httpClient = new HttpClient()
let backend = CloudBackendFactory.createIonQ httpClient workspaceUrl "ionq.simulator" 1000

// Async execution avoids blocking threads during cloud I/O
let executeOnCloud circuit (ct: CancellationToken) =
    task {
        let! result = backend.ExecuteToStateAsync circuit ct
        return result
    }
```

### Parallel Async Execution

Run multiple circuits concurrently using `Task.WhenAll`:

```fsharp
open System.Threading
open System.Threading.Tasks

let executeParallel (backend: IQuantumBackend) (circuits: ICircuit list) (ct: CancellationToken) =
    task {
        let tasks = 
            circuits
            |> List.map (fun c -> backend.ExecuteToStateAsync c ct)
            |> Array.ofList
        let! results = Task.WhenAll(tasks)
        return results |> Array.toList
    }
```

### UnifiedBackend Async Helpers

The `UnifiedBackend` module provides higher-level async utilities:

```fsharp
open FSharp.Azure.Quantum.Backends

// Apply a sequence of operations asynchronously
let! result = UnifiedBackend.applySequenceAsync backend operations initialState ct

// Apply with automatic state conversion
let! result = UnifiedBackend.applyWithConversionAsync backend operation state ct
```

## Cloud Backend Factory

Create cloud backends for different quantum hardware providers:

```fsharp
open System.Net.Http
open FSharp.Azure.Quantum.Backends

let httpClient = new HttpClient()
let workspaceUrl = "https://your-workspace.quantum.azure.com"

// Rigetti (superconducting qubits)
let rigetti = CloudBackendFactory.createRigetti httpClient workspaceUrl "rigetti.qvm" 1000

// IonQ (trapped ions)
let ionq = CloudBackendFactory.createIonQ httpClient workspaceUrl "ionq.simulator" 1000

// Quantinuum (trapped ions)
let quantinuum = CloudBackendFactory.createQuantinuum httpClient workspaceUrl "quantinuum.h1-1" 1000

// AtomComputing (neutral atoms)
let atomComputing = CloudBackendFactory.createAtomComputing httpClient workspaceUrl "atomcomputing.phoenix" 1000
```

All cloud backends implement the same `IQuantumBackend` interface (sync and async), so they are fully interchangeable with `LocalBackend`.

## Summary

**Current Implementation:**
- ✅ **Local simulation**: Fully functional (≤20 qubits, ~4 cities for TSP)
- ✅ **Unified API**: Same `solve` function for all backends (sync and async)
- ✅ **Async support**: Task-based async with CancellationToken on all backends
- ✅ **Cloud backends**: Rigetti, IonQ, Quantinuum, AtomComputing via CloudBackendFactory
- ⚠️ **Cloud integration**: Requires Azure Quantum workspace configuration

**Key Achievement:**
Backend switching is a **one-line code change** - no refactoring needed!

```fsharp
// Local simulation:
let backend = LocalBackendFactory.createUnified()

// Everything else stays the same!
match solve backend distances defaultConfig with
| Ok solution -> printfn "Solution: %A" solution
| Error err -> eprintfn "Error: %s" err.Message
```

**Benefits:**
- ✅ Write once, run anywhere (local or cloud)
- ✅ Test locally without Azure credentials
- ✅ No code changes needed to switch backends
- ✅ Same result format for analysis/visualization
- ✅ Production-ready quantum solving
- ✅ Async execution for non-blocking cloud I/O

## Next Steps

- **[Local Simulation Guide](local-simulation.md)** - Complete local simulator documentation
- **[Getting Started Guide](getting-started.md)** - Installation and first steps with backends
- **[API Reference](api-reference.md)** - Full API documentation

---

**Last Updated**: 2026-02-22  
**Status**: Current - Local and cloud backends supported with sync and async APIs
