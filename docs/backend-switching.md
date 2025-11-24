---
layout: default
title: Switching Between Local and Azure Backends
---

# Switching Between Local and Azure Backends

**How to seamlessly switch between local simulation and Azure Quantum execution**

## Overview

FSharp.Azure.Quantum supports two execution backends:

1. **Local Simulator** - Fast, free, offline simulation (up to 10 qubits)
2. **Azure Quantum** - Scalable cloud execution with real quantum hardware access

This guide shows you how to structure your code to easily switch between backends.

## Current State (v0.1.0-alpha)

### Separate APIs

Currently, local simulation and Azure execution use different APIs:

```fsharp
// Local simulation
open FSharp.Azure.Quantum.LocalSimulator

let localState = 
    StateVector.init 4
    |> QaoaSimulator.initializeUniformSuperposition
// ... apply gates ...
let samples = Measurement.sample localState 1000

// Azure Quantum (when available)
// open FSharp.Azure.Quantum.Azure
// let azureJob = AzureClient.submitJob circuit workspace
// let azureResults = AzureClient.getResults jobId
```

**This requires code changes to switch backends** ❌

## Recommended Pattern: Backend Abstraction

Until a unified API is available, use this abstraction pattern to make switching easy:

### Step 1: Define Common Types

```fsharp
module QuantumBackend =
    
    /// Measurement result format (common between backends)
    type MeasurementResult = {
        Counts: Map<string, int>
        Shots: int
        ExecutionTimeMs: float
    }
    
    /// Backend interface
    type IQuantumBackend =
        abstract member Execute: numQubits:int -> parameters:float[] -> costTerms:(int*int*float)[] -> shots:int -> Result<MeasurementResult, string>
```

### Step 2: Implement Local Backend

```fsharp
open FSharp.Azure.Quantum.LocalSimulator

type LocalBackend() =
    interface IQuantumBackend with
        member _.Execute numQubits parameters costTerms shots =
            try
                // Build QAOA circuit using local simulator
                let depth = parameters.Length / 2
                let (betas, gammas) = 
                    parameters 
                    |> Array.chunkBySize 2
                    |> Array.map (fun pair -> (pair.[0], pair.[1]))
                    |> Array.unzip
                
                // Initialize state
                let mutable state = QaoaSimulator.initializeUniformSuperposition numQubits
                
                // Apply QAOA layers
                for i in 0 .. depth - 1 do
                    let gamma = gammas.[i]
                    let beta = betas.[i]
                    
                    // Cost layer: Apply cost interactions
                    for (q1, q2, weight) in costTerms do
                        state <- QaoaSimulator.applyCostInteraction gamma q1 q2 weight state
                    
                    // Mixer layer
                    state <- QaoaSimulator.applyMixerLayer beta state
                
                // Measure
                let startTime = System.DateTime.UtcNow
                let samples = Measurement.sampleBitstrings state shots
                let elapsedMs = (System.DateTime.UtcNow - startTime).TotalMilliseconds
                
                Ok {
                    Counts = samples
                    Shots = shots
                    ExecutionTimeMs = elapsedMs
                }
            with
            | ex -> Error $"Local simulation failed: {ex.Message}"
```

### Step 3: Implement Azure Backend (Placeholder)

```fsharp
// open FSharp.Azure.Quantum.Azure

type AzureBackend(workspace: (* WorkspaceConfig *) unit) =
    interface IQuantumBackend with
        member _.Execute numQubits parameters costTerms shots =
            // TODO: When Azure integration is available
            Error "Azure backend not yet implemented - use LocalBackend for now"
            
            // Future implementation:
            // let circuit = buildQaoaCircuit numQubits parameters costTerms
            // let job = AzureClient.submitJob circuit workspace
            // let result = AzureClient.waitForCompletion job
            // Ok { Counts = result.Counts; Shots = shots; ExecutionTimeMs = result.Time }
```

### Step 4: Use Backend Abstraction

Now your algorithm code is backend-agnostic:

```fsharp
/// Solve MaxCut using specified backend
let solveMaxCut (backend: IQuantumBackend) (edges: (int*int) list) (numNodes: int) =
    // Convert edges to cost terms
    let costTerms = 
        edges 
        |> List.map (fun (i, j) -> (i, j, -1.0))
        |> Array.ofList
    
    // QAOA parameters (depth=1)
    let parameters = [| 0.5; 0.3 |]  // [beta, gamma]
    let shots = 1000
    
    // Execute on backend (local or Azure)
    backend.Execute numNodes parameters costTerms shots

// Switch backends with a single line change:

// Option 1: Local execution
let localBackend = LocalBackend() :> IQuantumBackend
match solveMaxCut localBackend edges 4 with
| Ok result -> 
    printfn "Local simulation: %A" result.Counts
| Error msg -> 
    eprintfn "Error: %s" msg

// Option 2: Azure execution (when available)
// let azureBackend = AzureBackend(workspace) :> IQuantumBackend
// match solveMaxCut azureBackend edges 4 with
// | Ok result -> printfn "Azure Quantum: %A" result.Counts
// | Error msg -> eprintfn "Error: %s" msg
```

## Configuration-Based Switching

Take it a step further with configuration-driven backend selection:

```fsharp
module Config =
    type BackendConfig =
        | Local
        | Azure of workspace: unit  // Replace 'unit' with actual WorkspaceConfig
    
    let createBackend (config: BackendConfig) : IQuantumBackend =
        match config with
        | Local -> LocalBackend() :> IQuantumBackend
        | Azure workspace -> AzureBackend(workspace) :> IQuantumBackend
    
    // Load from config file or environment
    let getBackendFromEnvironment () =
        match System.Environment.GetEnvironmentVariable("QUANTUM_BACKEND") with
        | "azure" -> 
            // Azure workspace (* todo: load from config *)
            Error "Azure backend not configured"
        | _ -> 
            Ok (LocalBackend() :> IQuantumBackend)

// Use in code:
match Config.getBackendFromEnvironment() with
| Ok backend ->
    let result = solveMaxCut backend edges numNodes
    // Process result
| Error msg ->
    eprintfn "Backend configuration error: %s" msg
```

Now you can switch backends via environment variable:

```bash
# Local execution (default)
dotnet run

# Azure execution (when implemented)
export QUANTUM_BACKEND=azure
dotnet run
```

## Hybrid Approach: Size-Based Auto-Selection

Automatically choose the best backend based on problem size:

```fsharp
let selectBackend (numQubits: int) : IQuantumBackend =
    if numQubits <= 10 then
        // Small problems: use fast local simulation
        printfn "Using local simulator (problem size: %d qubits)" numQubits
        LocalBackend() :> IQuantumBackend
    else
        // Large problems: require Azure
        printfn "Problem requires Azure Quantum (%d qubits > 10 qubit local limit)" numQubits
        // For now, fail gracefully
        failwith "Azure backend required but not available - reduce problem size to ≤10 qubits"
        // Future: AzureBackend(workspace) :> IQuantumBackend

// Automatic selection
let backend = selectBackend numNodes
let result = solveMaxCut backend edges numNodes
```

## Testing Strategy

Use local backend for tests, Azure for production:

```fsharp
module Tests =
    open Xunit
    
    [<Fact>]
    let ``MaxCut finds valid cut`` () =
        // Always use local backend in tests
        let backend = LocalBackend() :> IQuantumBackend
        let edges = [(0, 1); (1, 2); (2, 3); (3, 0)]
        
        match solveMaxCut backend edges 4 with
        | Ok result ->
            // Verify result quality
            Assert.True(result.Shots > 0)
            Assert.True(result.Counts.Count > 0)
        | Error msg ->
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
| **Production workloads** | ⚠️ Limited scale | ✅ Production-ready |
| **Real quantum hardware** | ❌ Simulation only | ✅ IonQ, Rigetti, etc. |
| **Parallel execution** | ❌ Single-threaded | ✅ Cloud parallelism |
| **Enterprise features** | ❌ Basic | ✅ Monitoring, logging, SLAs |

## Best Practices

### 1. Develop Locally, Deploy to Azure

```fsharp
// Development workflow
let devBackend = LocalBackend() :> IQuantumBackend

// Prototype and test locally
let parameters = optimizeParameters devBackend problem

// When ready, switch to Azure for production
// let prodBackend = AzureBackend(workspace) :> IQuantumBackend
// let finalResult = runOnBackend prodBackend parameters
```

### 2. Validate Locally Before Azure Submission

```fsharp
let validateThenExecute (problem: Problem) =
    // Step 1: Quick validation with local simulator
    let localBackend = LocalBackend() :> IQuantumBackend
    match execute localBackend problem 100 with  // Just 100 shots
    | Error msg -> 
        Error $"Local validation failed: {msg}"
    | Ok localResult ->
        printfn "✓ Local validation passed"
        
        // Step 2: Execute on Azure with full shots
        // let azureBackend = AzureBackend(workspace) :> IQuantumBackend
        // execute azureBackend problem 10000
        Ok localResult  // For now, return local result
```

### 3. Progressive Enhancement

```fsharp
let solveWithFallback (preferredBackend: IQuantumBackend) (fallbackBackend: IQuantumBackend) problem =
    match preferredBackend.Execute problem.NumQubits problem.Parameters problem.CostTerms 1000 with
    | Ok result -> 
        printfn "✓ Executed on preferred backend"
        Ok result
    | Error msg ->
        eprintfn "⚠ Preferred backend failed: %s" msg
        eprintfn "  Falling back to secondary backend..."
        fallbackBackend.Execute problem.NumQubits problem.Parameters problem.CostTerms 1000

// Try Azure, fall back to local if unavailable
// let azureBackend = AzureBackend(workspace) :> IQuantumBackend
let localBackend = LocalBackend() :> IQuantumBackend
// let result = solveWithFallback azureBackend localBackend problem
let result = solveWithFallback localBackend localBackend problem  // Both local for now
```

## Complete Example: Backend-Agnostic MaxCut Solver

```fsharp
module MaxCutSolver =
    
    /// MaxCut problem definition
    type MaxCutProblem = {
        NumNodes: int
        Edges: (int * int) list
    }
    
    /// Solution with backend info
    type MaxCutSolution = {
        Partition: string
        CutSize: int
        Probability: float
        Backend: string
        ExecutionTimeMs: float
    }
    
    /// Evaluate cut size for a bitstring
    let evaluateCut (edges: (int*int) list) (bitstring: string) =
        edges
        |> List.filter (fun (i, j) -> bitstring.[i] <> bitstring.[j])
        |> List.length
    
    /// Solve MaxCut problem on specified backend
    let solve (backend: IQuantumBackend) (problem: MaxCutProblem) (depth: int) =
        // Build cost terms
        let costTerms = 
            problem.Edges 
            |> List.map (fun (i, j) -> (i, j, -1.0))
            |> Array.ofList
        
        // QAOA parameters (simplified - would normally optimize)
        let parameters = Array.init (depth * 2) (fun i -> 
            if i % 2 = 0 then 0.5 else 0.3)  // Alternate beta/gamma
        
        // Execute on backend
        let startTime = System.DateTime.UtcNow
        match backend.Execute problem.NumNodes parameters costTerms 1000 with
        | Error msg -> Error msg
        | Ok result ->
            // Find best solution
            let (bestBitstring, bestCount) = 
                result.Counts 
                |> Map.toList 
                |> List.maxBy snd
            
            let cutSize = evaluateCut problem.Edges bestBitstring
            let probability = float bestCount / float result.Shots
            
            Ok {
                Partition = bestBitstring
                CutSize = cutSize
                Probability = probability
                Backend = backend.GetType().Name
                ExecutionTimeMs = result.ExecutionTimeMs
            }

// Usage
let problem = {
    NumNodes = 4
    Edges = [(0,1); (1,2); (2,3); (3,0); (0,2)]  // Square with diagonal
}

// Local execution
let localBackend = LocalBackend() :> IQuantumBackend
match MaxCutSolver.solve localBackend problem 1 with
| Ok solution ->
    printfn "Backend: %s" solution.Backend
    printfn "Best partition: %s" solution.Partition
    printfn "Cut size: %d / %d edges" solution.CutSize problem.Edges.Length
    printfn "Probability: %.1f%%" (solution.Probability * 100.0)
    printfn "Time: %.2f ms" solution.ExecutionTimeMs
| Error msg ->
    eprintfn "Solver failed: %s" msg

// Future: Azure execution (same code!)
// let azureBackend = AzureBackend(workspace) :> IQuantumBackend
// let solution = MaxCutSolver.solve azureBackend problem 2
```

## Summary

**Current State:**
- Local simulation: Fully functional for problems ≤10 qubits
- Azure integration: Planned for future release
- APIs: Currently separate, require code changes to switch

**Recommended Approach:**
1. Use the **backend abstraction pattern** shown above
2. Develop and test with **LocalBackend**
3. Structure code to be **backend-agnostic**
4. Prepare for Azure integration with **interface-based design**

**Benefits:**
- ✅ Easy to switch backends (one-line change)
- ✅ Testable without cloud access
- ✅ Future-proof for Azure integration
- ✅ Configuration-driven backend selection

## Next Steps

- **[Local Simulation Guide](local-simulation.md)** - Deep dive into local simulator
- **[API Reference](api-reference.md)** - Complete API documentation
- **[MaxCut Example](examples/maxcut-example.md)** - Complete MaxCut tutorial
- **[Azure Integration](azure-quantum.md)** - Azure Quantum setup (when available)

---

**Last Updated**: 2025-11-24  
**Status**: Local backend complete, Azure backend planned
