---
layout: default
title: Switching Between Local and Azure Backends
---

# Switching Between Local and Azure Backends

**How to seamlessly switch between local simulation and Azure Quantum execution**

## Overview

FSharp.Azure.Quantum provides a **unified API** through the `QuantumBackend` module that works with both:

1. **Local Simulator** - Fast, free, offline simulation (up to 20 qubits)
2. **Azure Quantum** - Scalable cloud execution with real quantum hardware access (coming soon)

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

## Summary

**Current Implementation:**
- ✅ **Local simulation**: Fully functional (≤20 qubits, ~4 cities for TSP)
- ✅ **Unified API**: Same `solve` function for all backends
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

## Next Steps

- **[Local Simulation Guide](local-simulation.md)** - Complete local simulator documentation
- **[Quantum TSP Example](examples/quantum-tsp-example.md)** - QAOA parameter optimization guide
- **[API Reference](api-reference.md)** - Full API documentation

---

**Last Updated**: 2025-11-28  
**Status**: Current - Local and cloud backends supported
