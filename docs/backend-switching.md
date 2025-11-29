---
layout: default
title: Switching Between Local and Azure Backends
---

# Switching Between Local and Azure Backends

**How to seamlessly switch between local simulation and Azure Quantum execution**

## Overview

FSharp.Azure.Quantum provides a **unified API** through the `QuantumBackend` module that works with both:

1. **Local Simulator** - Fast, free, offline simulation (up to 16 qubits)
2. **Azure Quantum** - Scalable cloud execution with real quantum hardware access (coming soon)

**Key Feature:** The same `QaoaCircuit` type is used for both backends, making backend switching a **one-line code change**.

## The Unified API

### Current Implementation 

The library provides a unified backend abstraction that works with both local simulation and cloud quantum backends:

```fsharp
open FSharp.Azure.Quantum.Quantum.QuantumTspSolver
open FSharp.Azure.Quantum.Core.BackendAbstraction

// Define TSP problem (3 cities)
let distances = array2D [
    [ 0.0; 1.0; 2.0 ]
    [ 1.0; 0.0; 1.5 ]
    [ 2.0; 1.5; 0.0 ]
]

// Method 1: Local simulator backend (up to 16 qubits)
let localBackend = createLocalBackend()
match solve localBackend distances defaultConfig with
| Ok solution -> printfn "Tour: %A, Length: %.2f" solution.Tour solution.TourLength
| Error msg -> printfn "Error: %s" msg

// Method 2: Cloud backend (IonQ/Rigetti)
let cloudBackend = createIonQBackend httpClient workspaceUrl "ionq.simulator"
match solve cloudBackend distances defaultConfig with
| Ok solution -> printfn "Tour: %A, Length: %.2f" solution.Tour solution.TourLength
| Error msg -> printfn "Error: %s" msg
```

**That's it!** Same solver API, different backends - just swap the backend creation.

## Simple Backend Switching

### Example: Switching with a Single Line

```fsharp
open FSharp.Azure.Quantum.Quantum.QuantumTspSolver
open FSharp.Azure.Quantum.Core.BackendAbstraction

/// Solve TSP problem with different backends
let solveTsp distances =
    // Define TSP problem (distance matrix already defined)
    
    // CHANGE THIS ONE LINE TO SWITCH BACKENDS:
    let backend = createLocalBackend()                 // ← Local simulation
    // let backend = createIonQBackend httpClient workspaceUrl "ionq.simulator"  // ← IonQ simulator
    // let backend = createIonQBackend httpClient workspaceUrl "ionq.qpu.aria-1" // ← Real quantum hardware
    
    // Same solver API for all backends
    solve backend distances defaultConfig

// Use it
let distances = array2D [
    [ 0.0; 1.0; 2.0 ]
    [ 1.0; 0.0; 1.5 ]
    [ 2.0; 1.5; 0.0 ]
]

match solveTsp distances with
| Ok solution ->
    printfn "Best tour: %A" solution.Tour
    printfn "Tour length: %.2f" solution.TourLength
| Error msg ->
    eprintfn "Error: %s" msg
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
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Quantum.QuantumTspSolver

module BackendConfig =
    
    type Config =
        | Local
        | IonQSimulator of httpClient: System.Net.Http.HttpClient * workspaceUrl: string
        | IonQHardware of httpClient: System.Net.Http.HttpClient * workspaceUrl: string
    
    let getBackend (config: Config) =
        match config with
        | Local -> 
            createLocalBackend()
        | IonQSimulator (httpClient, workspaceUrl) -> 
            createIonQBackend httpClient workspaceUrl "ionq.simulator"
        | IonQHardware (httpClient, workspaceUrl) ->
            createIonQBackend httpClient workspaceUrl "ionq.qpu.aria-1"
    
    // Load config from environment
    let fromEnvironment httpClient workspaceUrl =
        match System.Environment.GetEnvironmentVariable("QUANTUM_BACKEND") with
        | "ionq" -> IonQSimulator (httpClient, workspaceUrl)
        | "ionq-hardware" -> IonQHardware (httpClient, workspaceUrl)
        | _ -> Local  // Default: local simulator

// Usage
let config = BackendConfig.fromEnvironment httpClient workspaceUrl
let backend = BackendConfig.getBackend config
match solve backend distances defaultConfig with
| Ok solution -> printfn "Solution: %A" solution
| Error msg -> printfn "Error: %s" msg
```

Set backend via environment variable:

```bash
# Local execution
export QUANTUM_BACKEND=local
dotnet run

# IonQ simulator
export QUANTUM_BACKEND=ionq
dotnet run

# IonQ hardware
export QUANTUM_BACKEND=ionq-hardware
dotnet run

# Default (local)
dotnet run
```
            Assert.Fail($"Solver failed: {msg}")

module Production =
    let runProductionWorkload() =
        // Use Azure in production (when available)
        let backend = selectBackend 15  // Too large for local
        let result = solveMaxCut backend largeEdges 15
        // Process result
```

## Future: Unified High-Level API

**Current API ** - Already provides unified solving:

```fsharp
open FSharp.Azure.Quantum.Quantum.QuantumTspSolver
open FSharp.Azure.Quantum.Core.BackendAbstraction

let backend = createLocalBackend()  // or createIonQBackend, createRigettiBackend
match solve backend distances defaultConfig with
| Ok solution -> 
    printfn "Tour: %A" solution.Tour
    printfn "Length: %.2f" solution.TourLength
| Error msg -> 
    eprintfn "Error: %s" msg
```

This API provides:
- ✅ Automatic backend selection (local vs. cloud)
- ✅ Unified result format
- ✅ Error handling with Result type
- ✅ Easy backend switching (one-line change)

## Comparison: Local vs Azure

### When to Use Local Simulator

| Scenario | Local | Azure |
|----------|-------|-------|
| **Development** | ✅ Instant feedback | ❌ Network latency |
| **Unit Testing** | ✅ Fast, reliable | ❌ Slow, costs money |
| **Small problems** (≤16 qubits) | ✅ Free, fast | ❌ Overkill |
| **Offline work** | ✅ No internet needed | ❌ Requires connection |
| **Algorithm prototyping** | ✅ Rapid iteration | ❌ Slower iteration |

### When to Use Azure Quantum

| Scenario | Local | Azure |
|----------|-------|-------|
| **Large problems** (>16 qubits) | ❌ Not supported | ✅ Scales to 20+ qubits |
## Comparison: Local vs Azure

| Feature | Local Simulator | Azure Quantum |
|---------|----------------|---------------|
| **Qubit limit** | ≤16 qubits (65536 dimensions) | 100+ qubits (cloud) |
| **Cost** | Free | Pay per shot |
| **Network** | Offline capable | Requires internet |
| **Speed (3 cities)** | <100ms | Seconds (network + queue) |
| **Use cases** | Development, testing, small problems | Production, large problems, research |
| **Hardware access** | ❌ Simulation only | ✅ IonQ, Rigetti, etc. |

## Summary

**Current Implementation :**
- ✅ **Local simulation**: Fully functional (≤16 qubits, ~4 cities for TSP)
- ✅ **Cloud integration**: IonQ and Rigetti backends supported
- ✅ **Unified API**: Same `solve` function for all backends

**Key Achievement:**
Backend switching is a **one-line code change** - no refactoring needed!

```fsharp
// Change this:
let backend = createLocalBackend()

// To this:
let backend = createIonQBackend httpClient workspaceUrl "ionq.simulator"

// Everything else stays the same!
match solve backend distances defaultConfig with
| Ok solution -> (* use solution *)
| Error msg -> (* handle error *)
```

**Benefits:**
- ✅ Write once, run anywhere (local or cloud)
- ✅ Test locally without Azure credentials
- ✅ No code changes needed to switch backends
- ✅ Same result format for analysis/visualization
- ✅ Production-ready quantum solving

## Next Steps

- **[Local Simulation Guide](local-simulation.md)** - Complete local simulator documentation
- **[Quantum TSP Example](examples/quantum-tsp-example.md)** - QAOA parameter optimization guide
- **[API Reference](api-reference.md)** - Full API documentation

---

**Last Updated**: 2025-11-28  
**Status**: Current - Local and cloud backends supported
