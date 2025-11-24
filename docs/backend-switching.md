---
layout: default
title: Switching Between Local and Azure Backends
---

# Switching Between Local and Azure Backends

**How to seamlessly switch between local simulation and Azure Quantum execution**

## Overview

FSharp.Azure.Quantum provides a **unified API** through the `QuantumBackend` module that works with both:

1. **Local Simulator** - Fast, free, offline simulation (up to 10 qubits)
2. **Azure Quantum** - Scalable cloud execution with real quantum hardware access (coming soon)

**Key Feature:** The same `QaoaCircuit` type is used for both backends, making backend switching a **one-line code change**.

## The Unified API

### Current Implementation (v0.1.0-alpha)

The `QuantumBackend` module provides three ways to execute quantum circuits:

```fsharp
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Core.QuantumBackend
open FSharp.Azure.Quantum.Core.QaoaCircuit

// Create a circuit once
let circuit = {
    NumQubits = 3
    InitialStateGates = [| H(0); H(1); H(2) |]
    Layers = [|
        {
            CostGates = [| RZZ(0, 1, 0.5); RZZ(1, 2, 0.5) |]
            MixerGates = [| RX(0, 1.0); RX(1, 1.0); RX(2, 1.0) |]
            Gamma = 0.25
            Beta = 0.5
        }
    |]
    ProblemHamiltonian = ProblemHamiltonian.fromQubo quboMatrix
    MixerHamiltonian = MixerHamiltonian.create 3
}

// Method 1: Direct local execution
let localResult = Local.simulate circuit 1000

// Method 2: Direct Azure execution (when available)
// let azureResult = Azure.execute circuit 1000 workspace

// Method 3: Automatic selection based on size
let autoResult = autoExecute circuit 1000
// Uses Local for ≤10 qubits, Azure for >10 qubits
```

**That's it!** Same circuit, same result format, just different function calls.

## Simple Backend Switching

### Example: Switching with a Single Line

```fsharp
/// Solve MaxCut problem
let solveMaxCut edges numNodes shots =
    // Build QAOA circuit
    let quboMatrix = MaxCut.toQubo edges numNodes
    let circuit = {
        NumQubits = numNodes
        InitialStateGates = Array.init numNodes (fun i -> H(i))
        Layers = [|
            {
                CostGates = MaxCut.buildCostGates edges
                MixerGates = Array.init numNodes (fun i -> RX(i, 1.0))
                Gamma = 0.5
                Beta = 0.3
            }
        |]
        ProblemHamiltonian = ProblemHamiltonian.fromQubo quboMatrix
        MixerHamiltonian = MixerHamiltonian.create numNodes
    }
    
    // Execute on backend - CHANGE THIS ONE LINE TO SWITCH:
    Local.simulate circuit shots          // ← Local execution
    // Azure.execute circuit shots workspace  // ← Azure execution
    // autoExecute circuit shots             // ← Auto-select

// Use it
let edges = [(0, 1); (1, 2); (2, 0)]  // Triangle graph
match solveMaxCut edges 3 1000 with
| Ok result ->
    printfn "Backend: %s" result.Backend
    printfn "Best solution: %A" (result.Counts |> Map.toList |> List.maxBy snd)
| Error msg ->
    eprintfn "Error: %s" msg
```

### Uniform Result Format

All backends return the same `ExecutionResult` type:

```fsharp
type ExecutionResult = {
    /// Measurement counts (bitstring -> frequency)
    Counts: Map<string, int>
    
    /// Number of shots executed
    Shots: int
    
    /// Backend identifier ("Local", "Azure", etc.)
    Backend: string
    
    /// Execution time in milliseconds
    ExecutionTimeMs: float
    
    /// Job ID (Azure only, None for local)
    JobId: string option
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
module BackendConfig =
    
    type Config =
        | Local
        | Azure of workspace: unit  // TODO: Replace with actual WorkspaceConfig
        | Auto
    
    let getBackend (config: Config) (circuit: QaoaCircuit) (shots: int) =
        match config with
        | Local -> 
            Local.simulate circuit shots
        | Azure workspace -> 
            Azure.execute circuit shots workspace
        | Auto -> 
            autoExecute circuit shots
    
    // Load config from environment
    let fromEnvironment () =
        match System.Environment.GetEnvironmentVariable("QUANTUM_BACKEND") with
        | "azure" -> Azure ()  // TODO: Load workspace config
        | "local" -> Local
        | _ -> Auto  // Default: auto-select

// Usage
let config = BackendConfig.fromEnvironment()
let result = BackendConfig.getBackend config circuit 1000
```

Set backend via environment variable:

```bash
# Local execution
export QUANTUM_BACKEND=local
dotnet run

# Azure execution (when available)
export QUANTUM_BACKEND=azure
dotnet run

# Auto-select (default)
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

**Coming in future release** - A high-level unified API:

```fsharp
// Future API design (not yet implemented)
open FSharp.Azure.Quantum

let result = 
    Quantum.solve {
        problem = MaxCut edges
        backend = Auto  // or Local, or Azure workspace
        shots = 1000
        depth = 1
    }

match result with
| Ok solution -> 
    printfn "Used: %A backend" solution.Backend
    printfn "Result: %A" solution.BestSolution
| Error msg -> 
    eprintfn "Error: %s" msg
```

This API would:
- ✅ Automatically select best backend
- ✅ Provide unified result format
- ✅ Handle errors gracefully
- ✅ Support easy backend overrides

## Comparison: Local vs Azure

### When to Use Local Simulator

| Scenario | Local | Azure |
|----------|-------|-------|
| **Development** | ✅ Instant feedback | ❌ Network latency |
| **Unit Testing** | ✅ Fast, reliable | ❌ Slow, costs money |
| **Small problems** (≤10 qubits) | ✅ Free, fast | ❌ Overkill |
| **Offline work** | ✅ No internet needed | ❌ Requires connection |
| **Algorithm prototyping** | ✅ Rapid iteration | ❌ Slower iteration |

### When to Use Azure Quantum

| Scenario | Local | Azure |
|----------|-------|-------|
| **Large problems** (>10 qubits) | ❌ Not supported | ✅ Scales to 20+ qubits |
## Comparison: Local vs Azure

| Feature | Local Simulator | Azure Quantum |
|---------|----------------|---------------|
| **Qubit limit** | ≤10 qubits (1024 dimensions) | 100+ qubits (cloud) |
| **Cost** | Free | Pay per shot |
| **Network** | Offline capable | Requires internet |
| **Speed (10 qubits)** | <100ms | Seconds (network + queue) |
| **Use cases** | Development, testing, small problems | Production, large problems |
| **Hardware access** | ❌ Simulation only | ✅ IonQ, Rigetti, etc. |

## Summary

**Current Implementation (v0.1.0-alpha):**
- ✅ **Local simulation**: Fully functional (≤10 qubits)
- ⏳ **Azure integration**: Coming in future release
- ✅ **Unified API**: Same `QaoaCircuit` type for both backends

**Key Achievement:**
Backend switching is a **one-line code change** - no refactoring needed!

```fsharp
// Change this:
Local.simulate circuit 1000

// To this:
Azure.execute circuit 1000 workspace

// Everything else stays the same!
```

**Benefits:**
- ✅ Write once, run anywhere (local or cloud)
- ✅ Test locally without Azure credentials
- ✅ No code changes needed to switch backends
- ✅ Same result format for analysis/visualization
- ✅ Future-proof for when Azure backend is ready

## Next Steps

- **[Local Simulation Guide](local-simulation.md)** - Complete local simulator documentation
- **[QAOA Circuit Builder](qaoa-circuits.md)** - How to construct QAOA circuits
- **[Examples](examples/)** - MaxCut, TSP, and portfolio optimization examples
- **[API Reference](api-reference.md)** - Full API documentation

---

**Last Updated**: 2025-11-24  
**Status**: Local backend complete, Azure backend planned
