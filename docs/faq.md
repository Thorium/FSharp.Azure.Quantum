---
layout: default
title: FAQ
---

# Frequently Asked Questions (FAQ)

Common questions about FSharp.Azure.Quantum.

## 🔥 Quick Troubleshooting Guide

**Start here if something isn't working!**

| Symptom | Quick Fix |
|---------|-----------|
| "Distance matrix must be square" | Ensure rows = columns = number of cities |
| "Budget ... is insufficient to purchase any asset" | Budget must be ≥ cheapest asset price |
| Compiler error on `solution.Result` | Use `match` on the `Result` first (see [Getting Started](getting-started#complete-error-handling-examples)) |
| Error has no `.Contains` / `%s` rejects it | Errors are `QuantumError` values, not strings: use `err.Message` |
| Very slow first run | Normal - .NET JIT compilation. Second run will be fast. |
| Suboptimal solutions | Raise the QAOA shot counts in the solver config, or run several times and keep the best (see [below](#solutions-seem-suboptimal)) |
| "needs N qubits ... supports at most M" | The encoding is too wide for the backend; shrink the problem or use a larger backend (see [problem sizes](#what-problem-sizes-can-i-solve)) |
| Type inference error | Add explicit type annotations: `list<string * float * float>` |
| "MinHolding ... cannot exceed MaxHolding" | Check constraint values: `MinHolding ≤ MaxHolding ≤ Budget` |

**Still stuck?** See detailed [Errors and Troubleshooting](#errors-and-troubleshooting) below.

## General Questions

### What is FSharp.Azure.Quantum?

FSharp.Azure.Quantum is a **quantum-first F# library** for solving combinatorial optimization problems using quantum algorithms (QAOA, VQE, QFT). It provides:
- Quantum optimization algorithms (QAOA for graph problems, VQE for quantum chemistry)
- QFT-based algorithms (Shor's factorization, Phase Estimation)
- LocalBackend for free quantum simulation (width derived from available memory)
- Optional HybridSolver that picks a classical solver for small problems
- Integration with Azure Quantum cloud backends (IonQ, Rigetti, Quantinuum, Atom Computing, IQM) and D-Wave annealers
- High-level computation expression APIs for intuitive problem specification

### Do I need an Azure account to use this library?

**No!** The LocalBackend (default) provides quantum simulation entirely offline without Azure credentials. You only need Azure access if you want to use cloud quantum backends for real quantum hardware or provider simulators.

### Is this production-ready?

The current package version is **1.4.15**. It is suitable for:
- ✅ Development and prototyping
- ✅ Academic research and learning
- ✅ Quantum algorithm experimentation
- ✅ Applications whose problems fit the simulator or the cloud backends' qubit limits

**LocalBackend** provides free quantum simulation; its width is derived from available memory (hard ceiling 30 qubits), and iterative algorithms such as QAOA are practical up to about 20 qubits. For more, cloud backends are available via Azure Quantum.

## Technical Questions

### When should I use quantum vs HybridSolver?

#### Quick Comparison Table

| Aspect | Direct Quantum API | HybridSolver (with classical fallback) |
|--------|-------------------|----------------------------------------|
| **Approach** | QAOA/VQE quantum algorithms | `QuantumAdvisor` picks classical or quantum per problem |
| **When quantum runs** | Always | Only when the advisor strongly recommends it (≥ 50 variables by default) *and* you pass a backend, or when you force `Some HybridSolver.Quantum` |
| **Cost** | LocalBackend: free; Cloud: provider pricing | Classical runs are free; optional budget guard for quantum |
| **Problem Size** | Limited by the backend's qubit count (LocalBackend: memory-derived, QAOA practical to ~20 qubits) | Classical path has no qubit limit |
| **Best For** | Learning, consistent quantum API | Variable-size workloads where small cases should stay classical |
| **Backend** | LocalBackend (default) or a cloud backend | Same, passed to the `solve*WithBackend` functions |
| **Reproducible** | ⚠️ Probabilistic (quantum nature) | Classical path: ✅ Deterministic |

#### Decision Criteria

**Use Direct Quantum API when:**
- ✅ Learning quantum algorithms (QAOA, VQE, QFT)
- ✅ Want every run to go through a quantum backend
- ✅ The problem fits the backend (LocalBackend: memory-derived width)
- ✅ Developing/testing quantum algorithms

**Use HybridSolver when:**
- ⚡ Problem size varies significantly
- ⚡ Small problems should be solved classically, with the reasoning recorded
- ⚡ You want a budget guard on quantum cost

**Example:**
```fsharp
open FSharp.Azure.Quantum
// QuantumMaxCutSolver lives in the Quantum namespace
open FSharp.Azure.Quantum.Quantum

let problem =
    MaxCut.createProblem ["A"; "B"; "C"; "D"]
        [ ("A", "B", 1.0); ("B", "C", 2.0); ("C", "D", 1.0); ("D", "A", 1.0) ]

// Direct Quantum API: QAOA on the local simulator
match MaxCut.solve problem None with
| Ok solution -> printfn "Cut value: %.1f" solution.CutValue
| Error err -> eprintfn "Error: %s" err.Message

// HybridSolver takes the solver-level problem type
let hybridProblem: QuantumMaxCutSolver.MaxCutProblem =
    { Vertices = problem.Vertices; Edges = problem.Edges }

match HybridSolver.solveMaxCut hybridProblem None None None with
| Ok solution -> 
    printfn "Method: %A" solution.Method  // Classical or Quantum
    printfn "Reasoning: %s" solution.Reasoning
    printfn "Cut value: %.1f" solution.Result.CutValue
| Error err -> eprintfn "Error: %s" err.Message
```

**Crossover Point:** With `QuantumAdvisor.defaultThresholds`, HybridSolver routes to classical below 50 variables. From 50 up it uses quantum if you supplied a backend (and the estimated cost is within any budget you set); otherwise it still runs classically and says so in `Reasoning`.

### How accurate are the solutions?

**Quantum algorithms (QAOA/VQE)** provide:
- Approximate solutions (QAOA = Quantum Approximate Optimization Algorithm)
- Solution quality depends on circuit depth (p), shot count, and problem structure
- No optimality guarantee; compare against a classical solver on your own instances
- Probabilistic nature means running multiple times may yield better results

**Solution quality improves with:**
- Higher shot counts (e.g., 1000 vs 100 shots)
- Deeper circuits (higher QAOA depth p)
- Parameter optimization (variational loop)
- Error mitigation techniques (ZNE, PEC, REM)

**Classical path (via HybridSolver)** provides:
- Heuristic solutions (TSP: nearest neighbour + 2-opt; Portfolio: greedy by return/risk ratio)
- Deterministic results
- Fast execution for small and medium problems

### What problem sizes can I solve?

**With a quantum backend** the limit is the number of qubits the encoding needs:

| Problem | Qubits needed |
|---------|---------------|
| TSP | cities² (4 cities = 16 qubits) |
| Graph Coloring | nodes × colors |
| MaxCut, Knapsack, Portfolio | one per vertex / item / asset |
| Network Flow | one per route |
| Task Scheduling | tasks × time slots |

The local simulator holds up to `StateVector.maxQubits` (derived from memory, at most 30), and QAOA is practical up to about 20 qubits. Solvers return an error that names the qubit count when a problem is too wide for the backend.

**With the classical path of HybridSolver** there is no qubit limit: the classical TSP heuristic accepts up to 10,000 cities (`TspSolver.maxCities`).

### Can I use my own distance calculations?

Yes! Build the distance matrix yourself and pass it to `HybridSolver.solveTsp`:

```fsharp
open FSharp.Azure.Quantum

let cities = [| (0.0, 0.0); (1.0, 0.5); (2.0, 1.5); (3.0, 3.0) |]

// Custom distance function (Manhattan distance here)
let myDistance (x1, y1) (x2, y2) = abs (x1 - x2) + abs (y1 - y2)

// Build matrix
let n = cities.Length
let distances = 
    Array2D.init n n (fun i j ->
        if i = j then 0.0
        else myDistance cities.[i] cities.[j])

match HybridSolver.solveTsp distances None None None with
| Ok solution -> printfn "Tour length: %.2f (%A)" solution.Result.TourLength solution.Method
| Error err -> printfn "Error: %s" err.Message
```

## Errors and Troubleshooting

### "Distance matrix must be square"

**Problem:** Your distance matrix has different number of rows and columns.

**Solution:**
```fsharp
// ❌ Wrong: 3x2 matrix
let wrong = array2D [[0.0; 1.0]; [2.0; 0.0]; [3.0; 4.0]]

// ✅ Correct: 3x3 matrix
let correct = array2D [
    [0.0; 1.0; 2.0]
    [1.0; 0.0; 3.0]
    [2.0; 3.0; 0.0]
]
```

### "Distance matrix contains negative values"

**Problem:** Negative distances aren't supported.

**Solution:** Ensure all distances are >= 0.0:
```fsharp
// Normalize or shift if needed
let normalized = 
    distances 
    |> Array2D.map (fun d -> max 0.0 d)
```

### Solutions seem suboptimal

**Try these improvements:**

1. **Give QAOA more shots and iterations** (quantum TSP; 4 cities = 16 qubits):
```fsharp
open FSharp.Azure.Quantum.Quantum
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Backends.LocalBackend

let config =
    { QuantumTspSolver.defaultConfig with
        OptimizationShots = 500
        FinalShots = 4000
        MaxOptimizationIterations = 2000 }

let backend = LocalBackend() :> IQuantumBackend

match HybridSolver.solveTspWithBackendAndConfig distances None None (Some HybridSolver.Quantum) (Some backend) config with
| Ok solution -> printfn "Tour length: %.2f" solution.Result.TourLength
| Error err -> printfn "Error: %s" err.Message
```

2. **Compare with the classical heuristic:**
```fsharp
match HybridSolver.solveTsp distances None None (Some HybridSolver.SolverMethod.Classical) with
| Ok solution -> printfn "Tour length: %.2f" solution.Result.TourLength
| Error err -> printfn "Error: %s" err.Message
```

3. **Run the quantum solver several times and keep the best:**
```fsharp
[1..10]
|> List.choose (fun _ -> 
    match HybridSolver.solveTspWithBackend distances None None (Some HybridSolver.Quantum) (Some backend) with
    | Ok solution -> Some solution
    | Error _ -> None)
|> List.minBy (fun sol -> sol.Result.TourLength)
```

### How do I debug slow performance?

**Profile your problem:**

```fsharp
open System.Diagnostics

let sw = Stopwatch.StartNew()
match HybridSolver.solveTsp distances None None None with
| Ok solution -> 
    sw.Stop()
    printfn "Size: %d cities" (distances.GetLength(0))
    printfn "Time: %d ms" sw.ElapsedMilliseconds
    printfn "Method: %A" solution.Method
    printfn "Solver time: %.1f ms" solution.ElapsedMs
    printfn "2-opt iterations: %d" solution.Result.Iterations
| Error err -> printfn "Error: %s" err.Message
```

## Feature Questions

### Is quantum backend available yet?

**Status:** ✅ Quantum algorithms are implemented and available via LocalBackend (default) and Azure Quantum cloud backends (IonQ, Rigetti, Quantinuum, Atom Computing, IQM).

**Direct Quantum API:**
- QAOA for optimization problems (GraphColoring, MaxCut, Knapsack, TSP, Portfolio, NetworkFlow, Task Scheduling)
- VQE for quantum chemistry
- QFT-based algorithms (Shor's, Phase Estimation, Quantum Arithmetic)
- Runs on LocalBackend (free, memory-derived width) or cloud backends, where each algorithm submits complete circuits as whole-circuit jobs (every job billed; a `JobBudget` caps them)

**HybridSolver:** Chooses a classical solver for problems below the advisor's thresholds (50 variables by default), where quantum circuit overhead isn't beneficial.

### What quantum algorithms are used?

**Optimization Problems (QAOA-based):**
- GraphColoring: QAOA with K-coloring QUBO encoding
- MaxCut: QAOA with graph cut maximization
- Knapsack: QAOA with 0/1 knapsack constraints
- TSP: QAOA with tour feasibility constraints
- Portfolio: QAOA on the mean-variance QUBO −μᵀw + λ wᵀΣw (off-diagonal covariance terms when a covariance is given)
- NetworkFlow: QAOA with flow conservation

**Quantum Chemistry (VQE):**
- Variational Quantum Eigensolver for molecular ground state energies
- Supports custom Hamiltonians
- UCCSD ansatz: exact expectation values on a simulator; on a cloud backend, sampled whole circuits (one per commuting group of terms) optimised by SPSA
- Ground-state energies by quantum phase estimation (`GroundStateMethod.QPE`); the circuit is deep, so it is for simulators today

**QFT-Based Applications:**
- Shor's Algorithm: Period finding for integer factorization
- Phase Estimation: Eigenvalue extraction for quantum chemistry
- Quantum Arithmetic: Modular exponentiation using QFT

**Classical Fallback (HybridSolver only):**
- TSP: Nearest Neighbor + 2-opt local search
- Portfolio: Greedy selection by return/risk ratio (risk reported as sqrt(wᵀΣw) when a covariance is given)

### Can I add my own optimization problems?

Yes! The library is designed to be extensible:

1. Encode your problem as a QUBO (see [QUBO Encoding Strategies](qubo-encoding-strategies))
2. Run it with QAOA on any `IQuantumBackend` (see the `QaoaCircuit` / `QaoaOptimizer` modules and the existing solvers under `Solvers/Quantum`)
3. Use `QuantumAdvisor.getRecommendation` if you want a quantum-vs-classical recommendation

See [API Reference](api-reference) for the building blocks.

### Does it support GPU acceleration?

**Not in the local simulator**, which runs on the CPU. `CudaQBridge` can hand circuits to NVIDIA CUDA-Q as an external tool.

## Integration Questions

### How do I integrate with my existing F# code?

```fsharp
// Add package reference
// dotnet add package FSharp.Azure.Quantum

// Open namespace
open FSharp.Azure.Quantum

// Use in your code
let optimizeTour (distances: float[,]) =
    match HybridSolver.solveTsp distances None None None with
    | Ok solution -> Some solution.Result
    | Error _ -> None
```

### Can I use this from C#?

Yes! F# libraries are interoperable; F# `option` parameters take `null` for `None`:

```csharp
using FSharp.Azure.Quantum;

var distances = new double[,] {
    {0.0, 2.0, 9.0},
    {1.0, 0.0, 6.0},
    {15.0, 7.0, 0.0}
};

var result = HybridSolver.solveTsp(distances, null, null, null);

if (result.IsOk) {
    var solution = result.ResultValue;
    Console.WriteLine($"Tour length: {solution.Result.TourLength}");
} else {
    Console.WriteLine($"Error: {result.ErrorValue.Message}");
}
```

The `CSharpBuilders` class and the C# extension methods offer a more idiomatic surface for the problem builders; see [API Reference](api-reference#c-interop).

### Does it work with .NET 8/9/10?

**Targets:** .NET 10.0

**Compatible with:** .NET 10.0 or later

**Not compatible:** .NET Framework, .NET Core 3.1, .NET 5–9

## Cost and Licensing

### How much does it cost?

**Library:** Free and open source (Unlicense license)

**LocalBackend (Quantum Simulation):** Free - runs entirely local; width derived from available memory

**Cloud Quantum Backends:** Azure Quantum pricing applies
- Pay-per-use; cost depends on provider, circuit size and shot count
- Qubit limits the library enforces: IonQ Aria 25 / Forte 36, Rigetti 84, Quantinuum H1 32 / H2 56, Atom Computing 100, IQM 20; provider simulators 20
- `CostEstimation` gives rough per-provider estimates before you submit
- See [Azure Quantum Pricing](https://azure.microsoft.com/en-us/pricing/details/azure-quantum/)

### What license is it under?

**Unlicense** - public domain equivalent:
- ✅ Use commercially
- ✅ Modify freely
- ✅ No attribution required
- ✅ No warranty provided

## Support Questions

### Where can I get help?

- **GitHub Issues:** [Report bugs/request features](https://github.com/thorium/FSharp.Azure.Quantum/issues)
- **Documentation:** [Documentation home](index)
- **Examples:** [See examples/](https://github.com/Thorium/FSharp.Azure.Quantum/tree/main/examples)

### How do I report a bug?

1. Check [existing issues](https://github.com/thorium/FSharp.Azure.Quantum/issues)
2. Create minimal reproduction
3. Include:
   - F#/.NET version
   - Library version
   - Code sample
   - Expected vs actual behavior

### How can I contribute?

Contributions welcome!
- Fix bugs
- Add tests
- Improve documentation
- Suggest features

See `CONTRIBUTING.md` (if available) or open an issue to discuss.

## Performance Questions

### Why is my first call slow?

**.NET JIT compilation** - first call includes:
- Assembly loading
- JIT compilation
- Memory allocation

**Solution:** Warm up with a small problem first:
```fsharp
// Warm up JIT with a tiny problem
let _ = MaxCut.solve (MaxCut.createProblem ["A"; "B"] [ ("A", "B", 1.0) ]) None

// Now solve the real problem
let solution = MaxCut.solve problem None
```

### How do I parallelize multiple solves?

Use F# async; give each run its own backend instance:

```fsharp
// Ten independent QAOA runs; keep the best cut
let best = 
    [1..10]
    |> List.map (fun _ -> 
        async {
            let runBackend = LocalBackend() :> IQuantumBackend
            return MaxCut.solve problem (Some runBackend)
        })
    |> Async.Parallel
    |> Async.RunSynchronously
    |> Array.choose (function Ok s -> Some s | Error _ -> None)
    |> Array.maxBy (fun s -> s.CutValue)
```

Each run holds its own state vector, so memory grows with the number of parallel runs.

> **Async alternative:** The quantum solvers also have `solveAsync` variants (for example `QuantumMaxCutSolver.solveAsync`). See [Backend Switching](backend-switching) for more patterns.

---

## Still have questions?

- Check [Getting Started Guide](getting-started)
- Browse [Examples](https://github.com/Thorium/FSharp.Azure.Quantum/tree/main/examples)
- Review [API Reference](api-reference)
- Open a [GitHub Issue](https://github.com/thorium/FSharp.Azure.Quantum/issues)
