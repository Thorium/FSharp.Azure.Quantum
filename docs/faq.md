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
| "Budget insufficient" | Budget must be ≥ cheapest asset price |
| Compiler error on `solution.Result` | Use `match` on Result type (see [Error Handling](#complete-error-handling)) |
| Very slow first run | Normal - .NET JIT compilation. Second run will be fast. |
| Suboptimal solutions | Increase `MaxIterations` to 50000+ or run multiple times |
| Type inference error | Add explicit type annotations: `list<string * float * float>` |
| "MinHolding cannot exceed MaxHolding" | Check constraint values: `MinHolding ≤ MaxHolding ≤ Budget` |

**Still stuck?** See detailed [Errors and Troubleshooting](#errors-and-troubleshooting) below.

## General Questions

### What is FSharp.Azure.Quantum?

FSharp.Azure.Quantum is a **quantum-first F# library** for solving combinatorial optimization problems using quantum algorithms (QAOA, VQE, QFT). It provides:
- Quantum optimization algorithms (QAOA for graph problems, VQE for quantum chemistry)
- QFT-based algorithms (Shor's factorization, Phase Estimation)
- LocalBackend for free quantum simulation (up to 16 qubits)
- Optional HybridSolver with classical fallback for very small problems
- Integration with Azure Quantum cloud backends (IonQ, Rigetti)
- High-level computation expression APIs for intuitive problem specification

### Do I need an Azure account to use this library?

**No!** The LocalBackend (default) provides quantum simulation entirely offline without Azure credentials. You only need Azure access if you want to use cloud quantum backends (IonQ, Rigetti) for larger problems or real quantum hardware.

###Is this production-ready?

Currently **1.1.0** - suitable for:
- ✅ Production use (quantum algorithms via LocalBackend or cloud backends)
- ✅ Development and prototyping
- ✅ Academic research and learning
- ✅ Quantum algorithm experimentation

**LocalBackend** provides fast, free quantum simulation for problems up to 16 qubits. For larger problems, cloud backends (IonQ, Rigetti) are available via Azure Quantum.

## Technical Questions

### When should I use quantum vs HybridSolver?

#### Quick Comparison Table

| Aspect | Direct Quantum API | HybridSolver (with classical fallback) |
|--------|-------------------|----------------------------------------|
| **Approach** | QAOA/VQE quantum algorithms | Auto-routes: Quantum (≥20 vars) or Classical (< 20 vars) |
| **Speed** | LocalBackend: 1-10s, Cloud: 30-120s | Very small: <100ms, Others: same as quantum |
| **Cost** | LocalBackend: Free, Cloud: $10-100/run | Optimizes cost for very small problems |
| **Problem Size** | 5-200 variables (LocalBackend: ≤16 qubits) | 5-500 variables |
| **Best For** | Consistent quantum API, learning, fixed-size problems | Variable-size production workloads |
| **Availability** | ✅ Now (LocalBackend + Cloud) | ✅ Now |
| **Backend** | LocalBackend (default) or Cloud (IonQ/Rigetti) | Same, but optimizes very small problems |
| **Reproducible** | ⚠️ Probabilistic (quantum nature) | Classical fallback: ✅ Deterministic |

#### Decision Criteria

**Use Direct Quantum API when:**
- ✅ Learning quantum algorithms (QAOA, VQE, QFT)
- ✅ Problem size is consistent (e.g., always 50-100 variables)
- ✅ Want consistent quantum experience
- ✅ LocalBackend simulation is sufficient (≤16 qubits)
- ✅ Developing/testing quantum algorithms

**Use HybridSolver when:**
- ⚡ Problem size varies significantly (5 to 500 variables)
- ⚡ Want automatic classical fallback for very small problems (< 20 variables)
- ⚡ Performance optimization matters for variable-sized input
- ⚡ Production deployment with unpredictable problem sizes

**Example:**
```fsharp
// Direct Quantum API (Recommended for most cases)
match GraphColoring.solve problem 4 None with  // Uses QAOA on LocalBackend
| Ok solution -> printfn "Colors used: %d" solution.ColorsUsed
| Error msg -> eprintfn "Error: %s" msg

// HybridSolver (Optimizes very small problems automatically)
match HybridSolver.solveGraphColoring problem 4 None None None with
| Ok solution -> 
    printfn "Method: %A" solution.Method  // Classical or Quantum
    printfn "Reasoning: %s" solution.Reasoning
| Error msg -> eprintfn "Error: %s" msg
```

**Crossover Point:** HybridSolver routes to classical for < 20 variables, quantum for ≥ 20 variables. For most use cases, direct quantum API is simpler and sufficient.

### How accurate are the solutions?

**Quantum algorithms (QAOA/VQE)** provide:
- Approximate solutions (QAOA = Quantum Approximate Optimization Algorithm)
- Solution quality depends on circuit depth (p), shot count, and problem structure
- Typically finds good solutions (often within 5-15% of optimal for graph problems)
- Probabilistic nature means running multiple times may yield better results

**Solution quality improves with:**
- Higher shot counts (e.g., 1000 vs 100 shots)
- Deeper circuits (higher QAOA depth p)
- Parameter optimization (variational loop)
- Error mitigation techniques (ZNE, PEC, REM)

**Classical fallback (via HybridSolver)** provides:
- Heuristic solutions for very small problems (< 20 variables)
- Deterministic results
- Fast execution (< 100ms)
- Problem-specific tuning

### What problem sizes can I solve?

**TSP (Traveling Salesman):**
- ✅ Practical: 5-100 cities (classical)
- ⚠️ Possible: 100-500 cities (slower)
- ❌ Not recommended: >500 cities (use approximations)

**Portfolio Optimization:**
- ✅ Practical: 5-50 assets
- ⚠️ Possible: 50-200 assets
- ❌ Not recommended: >200 assets

**Performance scales approximately O(n²) for most problems.**

### Can I use my own distance calculations?

Yes! Just build a distance matrix:

```fsharp
// Custom distance function
let myDistance (city1, city2) = 
    // Your calculation here
    calculateCustomDistance city1 city2

// Build matrix
let distances = 
    Array2D.init n n (fun i j ->
        if i = j then 0.0
        else myDistance (cities.[i], cities.[j]))
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

1. **Increase iterations:**
```fsharp
let config = { TspSolver.defaultConfig with MaxIterations = 50000 }
```

2. **Adjust max iterations:**
```fsharp
match HybridSolver.solveTsp distances None None (Some HybridSolver.SolverMethod.Classical) with
| Ok solution -> printfn "Tour length: %.2f" solution.Result.TourLength
| Error msg -> printfn "Error: %s" msg
```

3. **Run multiple times with quantum optimization:**
```fsharp
[1..10]
|> List.map (fun _ -> 
    match HybridSolver.solveTsp distances None None None with
    | Ok solution -> Some solution
    | Error _ -> None)
|> List.choose id
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
| Error msg -> printfn "Error: %s" msg
```
printfn "Time per iteration: %.2f ms" (float sw.ElapsedMilliseconds / float solution.Iterations)
```

## Feature Questions

### Is quantum backend available yet?

**Status:** ✅ Quantum algorithms are fully implemented and available via LocalBackend (default) and Azure Quantum cloud backends (IonQ, Rigetti).

**Direct Quantum API:**
- QAOA for optimization problems (GraphColoring, MaxCut, Knapsack, TSP, Portfolio, NetworkFlow)
- VQE for quantum chemistry
- QFT-based algorithms (Shor's, Phase Estimation, Quantum Arithmetic)
- Runs on LocalBackend (free, up to 16 qubits) or cloud backends

**HybridSolver:** Adds classical fallback optimization for very small problems (< 20 variables) where quantum circuit overhead isn't beneficial yet.

### What quantum algorithms are used?

**Optimization Problems (QAOA-based):**
- GraphColoring: QAOA with K-coloring QUBO encoding
- MaxCut: QAOA with graph cut maximization
- Knapsack: QAOA with 0/1 knapsack constraints
- TSP: QAOA with tour feasibility constraints
- Portfolio: QAOA with budget and risk constraints  
- NetworkFlow: QAOA with flow conservation

**Quantum Chemistry (VQE):**
- Variational Quantum Eigensolver for molecular ground state energies
- Supports custom Hamiltonians
- Hardware-efficient ansatz circuits

**QFT-Based Applications:**
- Shor's Algorithm: Period finding for integer factorization
- Phase Estimation: Eigenvalue extraction for quantum chemistry
- Quantum Arithmetic: Modular exponentiation using QFT

**Classical Fallback (HybridSolver only):**
- TSP: Nearest Neighbor + 2-opt local search
- Portfolio: Greedy selection by return/risk ratio

### Can I add my own optimization problems?

Yes! The library is designed to be extensible:

1. Define your problem types
2. Implement solver logic
3. Integrate with HybridSolver
4. Use QuantumAdvisor for recommendations

See [API Reference](api-reference.md) for extensibility patterns.

### Does it support GPU acceleration?

**Currently:** No GPU support.

**Future:** May add GPU-accelerated solvers for larger problems.

## Integration Questions

### How do I integrate with my existing F# code?

```fsharp
// Add package reference
// dotnet add package FSharp.Azure.Quantum

// Open namespace
open FSharp.Azure.Quantum.Classical

// Use in your code
let optimizeTour cities distances =
    match HybridSolver.solveTsp distances None None None with
    | Ok solution -> Some solution.Result
    | Error _ -> None
```

### Can I use this from C#?

Yes! F# libraries are fully interoperable:

```csharp
using FSharp.Azure.Quantum.Classical;

var distances = new double[,] {
    {0.0, 2.0, 9.0},
    {1.0, 0.0, 6.0},
    {15.0, 7.0, 0.0}
};

var result = HybridSolver.solveTsp(
    distances, 
    FSharpOption<double>.None, 
    FSharpOption<double>.None, 
    FSharpOption<HybridSolver.SolverMethod>.None
);

if (FSharpResult<Solution, string>.get_IsOk(result)) {
    var solution = result.ResultValue;
    Console.WriteLine($"Tour length: {solution.Result.TourLength}");
}
```

### Does it work with .NET 8/9/10?

**Targets:** .NET 10.0

**Compatible with:** .NET 10.0 or later

**Not compatible:** .NET Framework, .NET Core 3.1, .NET 5/6/7

## Cost and Licensing

### How much does it cost?

**Library:** Free and open source (Unlicense license)

**LocalBackend (Quantum Simulation):** Free - runs entirely local, up to 16 qubits

**Cloud Quantum Backends:** Azure Quantum pricing applies
- Free tier available (limited shots)
- Pay-per-use for production ($10-100 per run depending on circuit complexity)
- IonQ: ~11 qubits QPU, 29+ qubits simulator
- Rigetti: ~80 qubits QPU, 40+ qubits simulator
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
- **Documentation:** [Complete docs](../README.md)
- **Examples:** [See examples/](examples/)

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
// Warm up JIT
let _ = TspSolver.solveWithDistances (array2D [[0.0]]) TspSolver.defaultConfig

// Now solve real problem
let solution = TspSolver.solveWithDistances largeDistances config
```

### How do I parallelize multiple solves?

Use F# async or parallel collections:

```fsharp
// Parallel solves with different seeds
let solutions = 
    [1..10]
    |> List.map (fun seed -> 
        async {
            let config = { TspSolver.defaultConfig with RandomSeed = Some seed }
            return TspSolver.solveWithDistances distances config
        })
    |> Async.Parallel
    |> Async.RunSynchronously
    |> Array.minBy (fun sol -> sol.TourLength)
```

---

## Still have questions?

- Check [Getting Started Guide](getting-started.md)
- Browse [Examples](examples/)
- Review [API Reference](api-reference.md)
- Open a [GitHub Issue](https://github.com/thorium/FSharp.Azure.Quantum/issues)
