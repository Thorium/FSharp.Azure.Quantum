---
layout: default
title: Switching Between Local and Azure Backends
---

# Switching Between Local and Azure Backends

**How to switch between local simulation and Azure Quantum execution**

## Overview

FSharp.Azure.Quantum provides a **unified API** through the `IQuantumBackend` interface (in `FSharp.Azure.Quantum.Core.BackendAbstraction`) that works with both:

1. **Local Simulator** - Fast, free, offline simulation (width derived from available memory, up to 30 qubits)
2. **Azure Quantum** - Cloud execution on simulators and real quantum hardware (requires an Azure subscription and a Quantum workspace)

**Key Feature:** Solvers and algorithms take an `IQuantumBackend` parameter, so switching backends is a **one-line code change**: construct a different backend and pass it in.

## The Unified API

### Current Implementation

The library provides a unified backend abstraction that works with both local simulation and cloud quantum backends:

```fsharp
open System.Threading
open FSharp.Azure.Quantum.Quantum.QuantumTspSolver
open FSharp.Azure.Quantum.Backends
open FSharp.Azure.Quantum.Core.BackendAbstraction

// Define TSP problem (3 cities)
let distances = array2D [
    [ 0.0; 1.0; 2.0 ]
    [ 1.0; 0.0; 1.5 ]
    [ 2.0; 1.5; 0.0 ]
]

/// Run the quantum TSP solver on any backend
let solveTsp (backend: IQuantumBackend) (distances: float[,]) =
    solveAsync backend distances defaultConfig CancellationToken.None
    |> Async.AwaitTask
    |> Async.RunSynchronously

// Local simulator backend (width derived from available memory)
let localBackend = LocalBackendFactory.createUnified()

match solveTsp localBackend distances with
| Ok solution -> printfn "Tour: %A, Length: %.2f" solution.Tour solution.TourLength
| Error err -> printfn "Error: %s" err.Message

// Cloud backends (IonQ, Rigetti, ...) are created with CloudBackendFactory - see below
```

**That's it!** Same solver API, different backends - just swap the backend creation.

## Simple Backend Switching

### Example: Switching with a Single Line

```fsharp
/// Solve TSP problem with a chosen backend
let solveWithChosenBackend distances =
    // CHANGE THIS ONE LINE TO SWITCH BACKENDS:
    let backend = LocalBackendFactory.createUnified()  // ← Local simulation
    // let backend = CloudBackends.CloudBackendFactory.createIonQ httpClient workspaceUrl "ionq.simulator" 1000

    // Same solver API for all backends
    solveTsp backend distances

// Use it
let distances2 = array2D [
    [ 0.0; 1.0; 2.0 ]
    [ 1.0; 0.0; 1.5 ]
    [ 2.0; 1.5; 0.0 ]
]

match solveWithChosenBackend distances2 with
| Ok solution ->
    printfn "Best tour: %A" solution.Tour
    printfn "Tour length: %.2f" solution.TourLength
| Error err ->
    eprintfn "Error: %s" err.Message
```

### Uniform Result Format

The TSP solver returns the same `QuantumTspSolution` record whichever backend it ran on (abridged):

```fsharp
type QuantumTspSolution = {
    /// Best tour found (city visit order)
    Tour: int array

    /// Total tour length (distance)
    TourLength: float

    /// Name of the backend that ran the circuits
    BackendName: string

    /// Number of measurement shots
    NumShots: int

    /// Wall-clock time in milliseconds
    ElapsedMs: float

    /// Optimized QAOA parameters (γ, β), when optimization ran
    OptimizedParameters: (float * float) option

    /// Whether the optimizer converged, when optimization ran
    OptimizationConverged: bool option

    /// Number of optimizer iterations, when optimization ran
    OptimizationIterations: int option

    // ... plus BestEnergy and TopSolutions
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
open System
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Backends.CloudBackends

module BackendConfig =

    type Config =
        | Local
        | IonQ of workspaceUrl: string * target: string

    let getBackend (config: Config) : IQuantumBackend =
        match config with
        | Local ->
            LocalBackendFactory.createUnified()
        | IonQ(workspaceUrl, target) ->
            // DefaultAzureCredential: `az login`, environment variables or managed identity
            let credential = Authentication.CredentialProviders.createDefaultCredential ()
            let httpClient = Authentication.createAuthenticatedClient credential
            CloudBackendFactory.createIonQ httpClient workspaceUrl target 1000

    // Load config from environment
    let fromEnvironment () =
        let backendName = Environment.GetEnvironmentVariable "QUANTUM_BACKEND"
        let workspaceUrl = Environment.GetEnvironmentVariable "AZURE_QUANTUM_WORKSPACE_URL"

        match backendName with
        | "ionq" when not (String.IsNullOrWhiteSpace workspaceUrl) -> IonQ(workspaceUrl, "ionq.simulator")
        | _ -> Local  // Default: local simulator

// Usage
let config = BackendConfig.fromEnvironment ()
let backend = BackendConfig.getBackend config
match solveTsp backend distances with
| Ok solution -> printfn "Solution: %A" solution
| Error err -> printfn "Error: %s" err.Message
```

Set backend via environment variable:

```bash
# Local execution (default)
export QUANTUM_BACKEND=local
dotnet run

# IonQ simulator on Azure Quantum (run `az login` first)
export QUANTUM_BACKEND=ionq
export AZURE_QUANTUM_WORKSPACE_URL=https://<location>.quantum.azure.com/subscriptions/<sub>/resourceGroups/<rg>/providers/Microsoft.Quantum/workspaces/<ws>
dotnet run
```

## Unified High-Level API

The same pattern holds for the other solvers and algorithms: each takes an `IQuantumBackend` (or an `IQuantumBackend option`, where `None` means the local simulator).

```fsharp
open FSharp.Azure.Quantum

// MaxCut with an explicit backend; pass None to use the local simulator
let square =
    MaxCut.createProblem
        [ "A"; "B"; "C"; "D" ]
        [ ("A", "B", 1.0); ("B", "C", 1.0); ("C", "D", 1.0); ("D", "A", 1.0) ]

match MaxCut.solve square (Some localBackend) with
| Ok solution ->
    printfn "Cut value: %.1f" solution.CutValue
    printfn "Partition S: %A" solution.PartitionS
| Error err ->
    eprintfn "Error: %s" err.Message
```

This API provides:
- ✅ Unified backend abstraction (local and cloud)
- ✅ Unified result format per solver
- ✅ Error handling with Result type
- ✅ Easy backend switching (one-line change)

## Comparison: Local vs Azure

### When to Use Local Simulator

| Scenario | Local | Azure |
|----------|-------|-------|
| **Development** | ✅ Instant feedback | ❌ Network latency |
| **Unit Testing** | ✅ Fast, reliable | ❌ Slow, costs money |
| **Small problems** (within simulator width) | ✅ Free, fast | ❌ Overkill |
| **Offline work** | ✅ No internet needed | ❌ Requires connection |
| **Algorithm prototyping** | ✅ Rapid iteration | ❌ Slower iteration |

### When to Use Azure Quantum

| Scenario | Local | Azure |
|----------|-------|-------|
| **Large problems** (beyond simulator width) | ❌ Not supported | ✅ Scales further |
| **Real hardware noise** | ⚠️ Only a simple depolarizing model (`NoisyLocalBackend`) | ✅ Actual device behaviour |
| **Hardware results** | ❌ Simulation only | ✅ IonQ, Rigetti, Quantinuum, etc. |

### Feature Comparison

| Feature | Local Simulator | Azure Quantum |
|---------|----------------|---------------|
| **Qubit limit** | memory-derived, ≤30 (2ⁿ × 16 bytes); solvers run up to 20 by default | Depends on the target device |
| **Cost** | Free | Pay per shot / job |
| **Network** | Offline capable | Requires internet |
| **Speed (3 cities)** | <100ms | Seconds to minutes (network + queue) |
| **Use cases** | Development, testing, small problems | Production, large problems, research |
| **Hardware access** | ❌ Simulation only | ✅ IonQ, Rigetti, etc. |

## Qubit Limit Awareness with IQubitLimitedBackend

### The IQubitLimitedBackend Interface

Some backends have a maximum number of qubits they can handle. The `IQubitLimitedBackend` interface provides a **non-breaking, opt-in extension** to `IQuantumBackend` that lets backends advertise their capacity. Its definition in `BackendAbstraction`:

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
- ✅ `LocalBackend` implements it with `MaxQubits = Some StateVector.maxQubits`, which is derived from available memory (at least 20, at most 30; override with the `FSAQ_MAX_QUBITS` environment variable)

A second optional interface, `IWallClockLimitedBackend`, reports `PracticalQubits`: the widest circuit worth running, as opposed to the widest state that fits in memory. `LocalBackend` reports `StateVector.practicalCircuitQubits` (20 by default; override with `FSAQ_MAX_CIRCUIT_QUBITS`).

### Querying Backend Limits

Use standard F# pattern matching to check whether a backend reports a qubit limit:

```fsharp
open FSharp.Azure.Quantum.Backends
open FSharp.Azure.Quantum.Core.BackendAbstraction

let limitedBackend = LocalBackendFactory.createUnified()

// Pattern match to discover qubit limits
let maxQubits =
    match limitedBackend with
    | :? IQubitLimitedBackend as lb -> lb.MaxQubits
    | _ -> None

match maxQubits with
| Some n -> printfn "Backend supports up to %d qubits" n
| None   -> printfn "Backend does not report a qubit limit"
```

### UnifiedBackend Helpers

For convenience, `UnifiedBackend` exposes helpers that wrap the pattern-match logic above:

- `UnifiedBackend.getMaxQubits backend` - the `IQubitLimitedBackend` capacity, or `None`
- `UnifiedBackend.getRunnableQubits backend` - the smaller of capacity and `PracticalQubits`, or `None` when the backend reports neither. Use this one to admit or refuse a problem.

```fsharp
let capacity = UnifiedBackend.getMaxQubits limitedBackend
// Some n for LocalBackend, where 20 <= n <= 30 depending on available memory

let runnable = UnifiedBackend.getRunnableQubits limitedBackend
// Some 20 for LocalBackend with default settings
```

### What Solvers Do With the Limit

Solvers check `UnifiedBackend.getRunnableQubits` before running:

- **TSP** needs N² qubits for N cities. If that exceeds the backend's runnable width, `solve`/`solveAsync` return a `ValidationError` that names the problem size and the limit; they do not split the problem. On the local simulator with default settings that means at most 4 cities.
- **Vertex cover, clique and matching** use `ProblemDecomposition.solveWithDecomposition`: when the problem is wider than the limit and the graph has several connected components, each component is solved separately on the same backend and the results are merged. A single component that is too wide is still run as a whole.
- **Set cover, SAT, bin packing and binary ILP** go through the same decomposition entry point, but they do not split problems yet.

```fsharp
open FSharp.Azure.Quantum.Quantum

// Two separate triangles: two connected components
let twoTriangles : QuantumVertexCoverSolver.Problem =
    { Vertices = [ for i in 0 .. 5 -> { Id = $"v{i}"; Weight = 1.0 } ]
      Edges = [ (0, 1); (1, 2); (2, 0); (3, 4); (4, 5); (5, 3) ] }

// If the graph were wider than the backend's runnable width, each triangle
// would be solved on its own and the covers combined.
match QuantumVertexCoverSolver.solve localBackend twoTriangles 1000 with
| Ok solution -> printfn "Cover: %A (valid: %b)" (solution.CoverVertices |> List.map _.Id) solution.IsValid
| Error err -> eprintfn "Error: %s" err.Message
```

## Async Backend Execution

All backends support **Task-based async execution** with `CancellationToken` support. This is especially important for cloud backends where network I/O introduces latency.

### IQuantumBackend Async Interface

The interface members, from `BackendAbstraction`:

| Member | Signature |
|--------|-----------|
| `ExecuteToState` | `ICircuit -> Result<QuantumState, QuantumError>` |
| `ApplyOperation` | `QuantumOperation -> QuantumState -> Result<QuantumState, QuantumError>` |
| `ExecuteToStateAsync` | `ICircuit -> CancellationToken -> Task<Result<QuantumState, QuantumError>>` |
| `ApplyOperationAsync` | `QuantumOperation -> QuantumState -> CancellationToken -> Task<Result<QuantumState, QuantumError>>` |
| `Name` | `string` |
| `NativeStateType` | `QuantumStateType` |
| `SupportsOperation` | `QuantumOperation -> bool` |
| `InitializeState` | `int -> Result<QuantumState, QuantumError>` |

`ICircuit` is the circuit interface in `Core.CircuitAbstraction`; wrap a `CircuitBuilder.Circuit` with `CircuitAbstraction.wrapCircuit`. Local backends complete these tasks synchronously; cloud backends submit a job and poll until it finishes.

### Async Usage Example

```fsharp
open System
open System.Threading
open System.Threading.Tasks
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.Core.CircuitAbstraction

// A 2-qubit Bell circuit, wrapped as an ICircuit
let bell =
    CircuitBuilder.empty 2
    |> CircuitBuilder.addGate (CircuitBuilder.H 0)
    |> CircuitBuilder.addGate (CircuitBuilder.CNOT(0, 1))
    |> wrapCircuit

// Use task { } computation expression for async workflows
let executeAsync (backend: IQuantumBackend) (circuit: ICircuit) (ct: CancellationToken) =
    task {
        let! result = backend.ExecuteToStateAsync circuit ct
        match result with
        | Ok state -> return Ok state
        | Error err -> return Error err
    }

// Run with cancellation support
let cts = new CancellationTokenSource(TimeSpan.FromSeconds(30.0))
let result =
    executeAsync localBackend bell cts.Token
    |> Async.AwaitTask |> Async.RunSynchronously
```

### Async with Cloud Backends

Cloud backends benefit most from async since they involve HTTP calls, job submission, and polling:

```fsharp
open FSharp.Azure.Quantum.Core

// Create an authenticated cloud backend via the factory
let workspaceUrl = "https://<location>.quantum.azure.com/subscriptions/<sub>/resourceGroups/<rg>/providers/Microsoft.Quantum/workspaces/<ws>"
let credential = Authentication.CredentialProviders.createDefaultCredential ()
let httpClient = Authentication.createAuthenticatedClient credential
let ionqBackend = CloudBackendFactory.createIonQ httpClient workspaceUrl "ionq.simulator" 1000

// Async execution avoids blocking threads during cloud I/O
let executeOnCloud (circuit: ICircuit) (ct: CancellationToken) =
    task {
        let! result = ionqBackend.ExecuteToStateAsync circuit ct
        return result
    }
```

Cloud backends turn the returned measurement histogram into an approximate state, so read results with `Primitives.sample`, which returns the job's own counts, rather than relying on amplitudes. Such a state carries the job's recorded counts: `UnifiedBackend.measureState` on it returns the job's own shots, drawn without replacement and never more than the job measured, so the array can be shorter than the number requested; `UnifiedBackend.recordedShots` returns all of them.

### Parallel Async Execution

Run multiple circuits concurrently using `Task.WhenAll`:

```fsharp
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

`Primitives.sampleBatchAsync` and `Primitives.observeBatchAsync` do the same for `CircuitBuilder` circuits and return histograms or expectation values.

### UnifiedBackend Async Helpers

The `UnifiedBackend` module provides higher-level async utilities. They apply operations one at a time, so they need a backend that accepts incremental `ApplyOperation` (the local simulators and the topological backend); a cloud backend returns an `Error` for them, and takes the same gates as one circuit through `ExecuteToStateAsync` or `UnifiedBackend.submitAsCircuit`:

```fsharp
open FSharp.Azure.Quantum.Core

// Apply a sequence of operations asynchronously, starting from |00⟩
let applyBellAsync (backend: IQuantumBackend) (ct: CancellationToken) =
    task {
        match backend.InitializeState 2 with
        | Error err -> return Error err
        | Ok initialState ->
            let operations =
                [ QuantumOperation.Gate(CircuitBuilder.H 0)
                  QuantumOperation.Gate(CircuitBuilder.CNOT(0, 1)) ]

            return! UnifiedBackend.applySequenceAsync backend operations initialState ct
    }

// Apply one operation, converting the state to the backend's native representation if needed
let applyXAsync (backend: IQuantumBackend) (state: QuantumState) (ct: CancellationToken) =
    UnifiedBackend.applyWithConversionAsync backend (QuantumOperation.Gate(CircuitBuilder.X 0)) state ct
```

## Cloud Backend Factory

Create cloud backends for different quantum hardware providers. Each factory function takes an authenticated `HttpClient`, the workspace URL, a target name and a shot count:

```fsharp
// Rigetti (superconducting qubits)
let rigetti = CloudBackendFactory.createRigetti httpClient workspaceUrl "rigetti.sim.qvm" 1000

// IonQ (trapped ions)
let ionq = CloudBackendFactory.createIonQ httpClient workspaceUrl "ionq.simulator" 1000

// Quantinuum (trapped ions)
let quantinuum = CloudBackendFactory.createQuantinuum httpClient workspaceUrl "quantinuum.sim.h1-1sc" 1000

// Atom Computing (neutral atoms)
let atomComputing = CloudBackendFactory.createAtomComputing httpClient workspaceUrl "atom-computing.sim" 1000

// IQM (superconducting qubits)
let iqm = CloudBackendFactory.createIqm httpClient workspaceUrl "iqm.sim" 1000
```

Hardware targets follow the same pattern, for example `"ionq.qpu.aria-1"`, `"rigetti.qpu.ankaa-3"`, `"quantinuum.qpu.h1-1"`, `"atom-computing.qpu.phoenix"` or `"iqm.qpu.garnet"`; check your workspace for the targets it offers. `CloudBackendFactory.createRigettiRouted` additionally takes a device coupling map and inserts SWAP gates so two-qubit gates respect the hardware connectivity.

All cloud backends implement the same `IQuantumBackend` interface (sync and async), so they are interchangeable with `LocalBackend`.

What differs on cloud backends:

- **Whole circuits only.** They refuse incremental `ApplyOperation` and claim no algorithm intent (QFT, QPE, Grover…), so algorithms build the complete gate circuit and submit it with `ExecuteToState`. Before conversion each backend transpiles the circuit to its provider's gates (`GateTranspiler.transpileForBackendFully`): T/TDG, CP, CRZ, CCX, MCZ and the other composite gates are decomposed, and the Braket backend does the same by device ARN.
- **Measured shots, not amplitudes.** They implement `IShotSamplingBackend`: the returned state holds √(count/shots) with no phases. `Primitives.observe` therefore measures each qubit-wise commuting group of Pauli terms in its own rotated basis (`Primitives.sampledExpectation`, one job per group, with a standard error), ADAPT-VQE and ADAPT-QAOA switch to measured energies with parameter-shift gradients, and `Primitives.sample` returns the backend's own counts (the requested shot count must equal the backend's). `QRNG.generateWithBackend` needs a backend created with `shots = 1`: its bits are that one measured shot. Algorithms that measure the returned state (`UnifiedBackend.measureState`) get the job's own recorded shots, never resampled ones and never more than the job measured. Protocols made of many independent trials (BB84 transmissions, E91 pairs, teleportation tomography) run their trials side by side in circuits as wide as the backend runs (at most 16 qubits, `WholeCircuit.runTrials`) and use every shot of every job.
- **Every `ExecuteToState` is a separately billed job.** Iterative algorithms submit many (one per energy, gradient term or sample; a 3-city TSP by QAOA is several hundred). Pass a `JobBudget` to cap them; the job after the limit is refused with a `QuotaExceeded` error before it is submitted. A budget can be shared by several backends, and every cloud backend exposes its budget through `IJobCountingBackend`, including how many jobs it has submitted. Without one, jobs are counted but not limited.

```fsharp
open FSharp.Azure.Quantum.Backends

let budget = CloudBackendHelpers.JobBudget.Limit 200

let limitedIonQ =
    CloudBackends.IonQCloudBackend(httpClient, workspaceUrl, "ionq.simulator", 1000, jobBudget = budget)

// ... run an algorithm on limitedIonQ ...
printfn "Jobs submitted: %d of %A" budget.Submitted budget.MaxJobs
```

## Summary

**Current Implementation:**
- ✅ **Local simulation**: Fully functional (memory-derived width; TSP up to 4 cities with default settings)
- ✅ **Unified API**: The same solver calls work with every backend (sync and async)
- ✅ **Async support**: Task-based async with CancellationToken on all backends
- ✅ **Cloud backends**: Rigetti, IonQ, Quantinuum, Atom Computing and IQM via `CloudBackends.CloudBackendFactory`
- ✅ **Algorithms on cloud**: QAOA solvers, chemistry VQE and QPE, Grover and its builders, amplitude amplification, QFT, QPE, Shor, HHL (magnitudes; signs and phases with `HHL.executeWithRelativePhases`, which HHL regression uses), arithmetic, ADAPT-VQE/ADAPT-QAOA, QML, quantum Monte Carlo and `Primitives` submit whole circuits; a `JobBudget` caps the billed jobs
- ⚠️ **Cloud integration**: Requires Azure Quantum workspace configuration and credentials

**Key Achievement:**
Backend switching is a **one-line code change** - no refactoring needed!

```fsharp
// Local simulation:
let chosenBackend = LocalBackendFactory.createUnified()

// Everything else stays the same!
match solveTsp chosenBackend distances with
| Ok solution -> printfn "Solution: %A" solution
| Error err -> eprintfn "Error: %s" err.Message
```

**Benefits:**
- ✅ Write once, run anywhere (local or cloud): the solvers and algorithms pick the whole-circuit route on a cloud backend themselves. Only code that continues from a returned state with `ApplyOperation` needs a simulator
- ✅ Test locally without Azure credentials
- ✅ No code changes needed to switch backends
- ✅ Same result format for analysis/visualization
- ✅ Async execution for non-blocking cloud I/O

## Next Steps

- **[Local Simulation Guide](local-simulation.md)** - Complete local simulator documentation
- **[Getting Started Guide](getting-started.md)** - Installation and first steps with backends
- **[API Reference](api-reference.md)** - Full API documentation

---

**Last Updated**: 2026-09-30  
**Status**: Current - Local and cloud backends supported with sync and async APIs
