# Frequently Asked Questions (FAQ)

Common questions about FSharp.Azure.Quantum.

## General Questions

### What is FSharp.Azure.Quantum?

FSharp.Azure.Quantum is an F# library for quantum-inspired optimization using Azure Quantum services. It provides:
- Classical optimization algorithms (TSP, Portfolio)
- Hybrid solver that automatically chooses quantum or classical
- Integration with Azure Quantum for quantum backend access
- Builder pattern APIs for fluent problem construction

### Do I need an Azure account to use this library?

**No!** The classical solvers work entirely offline without any Azure credentials. You only need Azure access if you want to use actual quantum backends (coming soon).

###Is this production-ready?

Currently **0.1.0-alpha** - suitable for:
- ✅ Development and prototyping
- ✅ Academic research
- ✅ Learning quantum-inspired algorithms
- ⚠️ Production use (test thoroughly first)

## Technical Questions

### When should I use quantum vs classical?

**Use Classical when:**
- Problem size < 100 variables
- Need immediate results (ms response time)
- Developing/testing locally
- Cost is a concern

**Consider Quantum when:**
- Problem size > 100 variables
- Problem has special structure (QUBO-compatible)
- Can tolerate longer wait times
- Budget available for quantum execution

**Use HybridSolver to decide automatically:**
```fsharp
HybridSolver.solveTsp distances None None None  // Automatic decision
```

### How accurate are the solutions?

**Classical solvers** provide:
- Heuristic solutions (not guaranteed optimal)
- Typically within 5-10% of optimal for TSP
- Better with more iterations and tuning

**Solution quality improves with:**
- Higher `MaxIterations` (e.g., 50000 vs 10000)
- Better cooling schedule (slower = better)
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

2. **Adjust cooling rate:**
```fsharp
let config = { TspSolver.defaultConfig with CoolingRate = 0.999 }
```

3. **Run multiple times with different seeds:**
```fsharp
[1..10]
|> List.map (fun seed -> 
    let config = { TspSolver.defaultConfig with RandomSeed = Some seed }
    TspSolver.solveWithDistances distances config)
|> List.minBy (fun sol -> sol.TourLength)
```

### How do I debug slow performance?

**Profile your problem:**

```fsharp
open System.Diagnostics

let sw = Stopwatch.StartNew()
let solution = TspSolver.solveWithDistances distances config
sw.Stop()

printfn "Size: %d cities" distances.GetLength(0)
printfn "Iterations: %d" solution.Iterations
printfn "Time: %d ms" sw.ElapsedMilliseconds
printfn "Time per iteration: %.2f ms" (float sw.ElapsedMilliseconds / float solution.Iterations)
```

## Feature Questions

### Is quantum backend available yet?

**Status:** Quantum routing is implemented in HybridSolver but quantum backends are not yet connected.

**Currently:** HybridSolver uses classical fallback for all problems.

**Coming soon:** Azure Quantum backend integration for actual quantum execution.

### What optimization algorithms are used?

**TSP:**
- Simulated Annealing with adaptive cooling
- 2-opt local search improvements
- Random restart heuristics

**Portfolio:**
- Greedy selection by return/risk ratio
- Constraint satisfaction filtering
- Knapsack-style optimization

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

**Classical solvers:** Free - run entirely local

**Quantum backends (future):** Azure Quantum pricing applies
- Free tier available
- Pay-per-use for production
- See [Cost Management Guide](guides/cost-management.md)

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
