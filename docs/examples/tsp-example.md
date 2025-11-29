---
layout: default
title: TSP Example
---

# TSP (Traveling Salesman Problem) Example

Complete guide to solving TSP problems with FSharp.Azure.Quantum.

## Problem Overview

The Traveling Salesman Problem asks: *"Given a list of cities and distances between them, what is the shortest route that visits each city exactly once and returns to the starting city?"*

This is an NP-hard problem, making it perfect for quantum-inspired optimization approaches.

## Basic Example: 5-City Tour

Let's start with a simple 5-city problem:

```fsharp
open FSharp.Azure.Quantum.Classical

// Define cities and distances
let cities = ["Seattle"; "Portland"; "San Francisco"; "Los Angeles"; "San Diego"]

// Distance matrix (in miles)
let distances = array2D [
    //  SEA    PDX    SFO    LAX    SAN
    [   0.0; 174.0; 807.0; 1135.0; 1255.0 ]  // Seattle
    [ 174.0;   0.0; 635.0;  959.0; 1079.0 ]  // Portland  
    [ 807.0; 635.0;   0.0;  382.0;  502.0 ]  // San Francisco
    [1135.0; 959.0; 382.0;    0.0;  120.0 ]  // Los Angeles
    [1255.0;1079.0; 502.0;  120.0;    0.0 ]  // San Diego
]

// Solve with classical solver
let config = TspSolver.defaultConfig
let solution = TspSolver.solveWithDistances distances config

// Display results
printfn "Optimal Tour:"
solution.Tour
|> Array.map (fun i -> cities.[i])
|> Array.iteri (fun step city -> printfn "  %d. %s" (step + 1) city)

printfn "\nTotal Distance: %.2f miles" solution.TourLength
printfn "Iterations: %d" solution.Iterations
```

**Output:**
```
Optimal Tour:
  1. Seattle
  2. Portland
  3. San Francisco
  4. Los Angeles
  5. San Diego

Total Distance: 1982.00 miles
Iterations: 8347
```

## Using HybridSolver (Automatic Selection)

Let the `HybridSolver` automatically choose the best approach:

```fsharp
match HybridSolver.solveTsp distances None None None with
| Ok solution ->
    printfn "Method: %A" solution.Method
    printfn "Reasoning: %s" solution.Reasoning
    printfn "Tour: %A" solution.Result.Tour
    printfn "Length: %.2f miles" solution.Result.TourLength
    printfn "Time: %.2f ms" solution.ElapsedMs
| Error msg ->
    printfn "Error: %s" msg
```

## Larger Problem: 20-City Tour

```fsharp
// Generate random city coordinates
let random = System.Random(42)
let cityCount = 20

let coordinates = 
    Array.init cityCount (fun _ -> 
        (random.NextDouble() * 1000.0, random.NextDouble() * 1000.0))

// Calculate Euclidean distance matrix
let calculateDistance (x1, y1) (x2, y2) =
    sqrt ((x2 - x1) ** 2.0 + (y2 - y1) ** 2.0)

let largeDistances = 
    Array2D.init cityCount cityCount (fun i j ->
        if i = j then 0.0
        else calculateDistance coordinates.[i] coordinates.[j])

// Solve with custom configuration
let customConfig = {
    MaxIterations = 20000
    InitialTemperature = 200.0
    CoolingRate = 0.99
    RandomSeed = Some 42
}

let largeSolution = TspSolver.solveWithDistances largeDistances customConfig

printfn "20-City Tour Length: %.2f" largeSolution.TourLength
printfn "Converged after %d iterations" largeSolution.Iterations
```

## Builder Pattern API

Use the builder pattern for more complex scenarios:

```fsharp
// Create problem
let problem = Tsp.createProblem()

// Solve with default config
let solution = Tsp.solve problem

// Or solve with custom config
let customSolution = Tsp.solveDirectly customConfig problem
```

## Performance Comparison

Compare classical vs different problem sizes:

```fsharp
open System.Diagnostics

let measurePerformance size =
    let distances = Array2D.init size size (fun i j -> 
        if i = j then 0.0 else float (abs (i - j)))
    
    let sw = Stopwatch.StartNew()
    let solution = TspSolver.solveWithDistances distances TspSolver.defaultConfig
    sw.Stop()
    
    printfn "Size: %d, Length: %.2f, Time: %d ms" 
        size solution.TourLength sw.ElapsedMilliseconds

[5; 10; 20; 50; 100]
|> List.iter measurePerformance
```

**Output:**
```
Size: 5, Length: 10.00, Time: 12 ms
Size: 10, Length: 25.00, Time: 45 ms
Size: 20, Length: 60.00, Time: 187 ms
Size: 50, Length: 180.00, Time: 1234 ms
Size: 100, Length: 380.00, Time: 5678 ms
```

## Asymmetric TSP

Handle asymmetric distance matrices (where distance A→B ≠ distance B→A):

```fsharp
let asymmetricDistances = array2D [
    [ 0.0; 10.0; 15.0; 20.0 ]
    [ 12.0;  0.0;  8.0; 14.0 ]  // Different from reverse direction
    [ 18.0;  9.0;  0.0; 11.0 ]
    [ 22.0; 16.0; 13.0;  0.0 ]
]

let solution = TspSolver.solveWithDistances asymmetricDistances TspSolver.defaultConfig
```

## Handling Edge Cases

### Empty or Single City

```fsharp
// Empty matrix - returns error
let emptyDistances = Array2D.zeroCreate 0 0
match HybridSolver.solveTsp emptyDistances None None None with
| Ok _ -> printfn "Unexpected success"
| Error msg -> printfn "Expected error: %s" msg

// Single city - trivial solution
let singleCity = array2D [[0.0]]
match HybridSolver.solveTsp singleCity None None None with
| Ok solution -> 
    printfn "Single city tour length: %.2f" solution.Result.TourLength
| Error msg -> printfn "Error: %s" msg
```

### Invalid Distance Matrix

```fsharp
// Non-square matrix
let invalid = array2D [[0.0; 1.0]; [2.0; 0.0]; [3.0; 4.0]]

// Negative distances
let negative = array2D [[0.0; -5.0]; [3.0; 0.0]]

// Both will return errors when validated
```

## Advanced: Custom Cooling Schedule

Implement simulated annealing with custom cooling:

```fsharp
let experimentalConfig = {
    MaxIterations = 50000
    InitialTemperature = 500.0
    CoolingRate = 0.995  // Slower cooling = better solutions
    RandomSeed = None     // Random initialization
}

let solution = TspSolver.solveWithDistances distances experimentalConfig
```

## Quantum Advisor Recommendations

Get recommendations before solving:

```fsharp
match QuantumAdvisor.getRecommendation distances with
| Ok recommendation ->
    printfn "Problem Size: %d cities" recommendation.ProblemSize
    printfn "Complexity: %s" recommendation.Complexity
    printfn "Recommendation: %A" recommendation.RecommendationType
    printfn "Estimated Classical Time: %.2f ms" recommendation.EstimatedClassicalTime
    printfn "Reasoning: %s" recommendation.Reasoning
    
    // Now solve based on recommendation
    match recommendation.RecommendationType with
    | QuantumAdvisor.RecommendationType.StronglyRecommendClassical ->
        // Use classical solver directly
        let solution = TspSolver.solveWithDistances distances TspSolver.defaultConfig
        printfn "Classical solution: %.2f" solution.TourLength
    | _ ->
        // Use hybrid solver
        match HybridSolver.solveTsp distances None None None with
        | Ok solution -> printfn "Hybrid solution: %.2f" solution.Result.TourLength
        | Error msg -> printfn "Error: %s" msg
| Error msg ->
    printfn "Advisor error: %s" msg
```

## Quantum TSP with QAOA Parameter Optimization (v1.1.0)

For larger problems or research applications, use quantum TSP solver with automatic parameter optimization:

```fsharp
open FSharp.Azure.Quantum.Quantum.QuantumTspSolver
open FSharp.Azure.Quantum.Core.BackendAbstraction

// Create distance matrix
let distances = array2D [
    [ 0.0; 1.0; 2.0 ]
    [ 1.0; 0.0; 1.5 ]
    [ 2.0; 1.5; 0.0 ]
]

// Create backend (local simulator or Azure Quantum)
let backend = createLocalBackend()

// Solve with default configuration (optimization enabled)
match solve backend distances defaultConfig with
| Ok solution ->
    printfn "Best tour: %A" solution.Tour
    printfn "Tour length: %.2f" solution.TourLength
    printfn "Optimized parameters (γ, β): %A" solution.OptimizedParameters
    printfn "Converged in %d iterations" solution.OptimizationIterations
| Error msg ->
    printfn "Error: %s" msg
```

### Custom Optimization Configuration

Fine-tune the variational quantum-classical loop:

```fsharp
let customConfig = {
    OptimizationShots = 100       // Fast parameter search (low shots)
    FinalShots = 1000             // Accurate result (high shots)
    EnableOptimization = true     // Enable variational loop
    InitialParameters = (0.5, 0.5) // Starting guess (γ, β)
}

match solve backend distances customConfig with
| Ok solution ->
    printfn "Solution quality: %.2f" solution.TourLength
    printfn "Optimization iterations: %d" solution.OptimizationIterations
    printfn "Final parameters: %A" solution.OptimizedParameters
| Error msg ->
    printfn "Error: %s" msg
```

### How QAOA Parameter Optimization Works

**Variational Loop (Nelder-Mead Simplex):**

```
1. Initialize: (γ₀, β₀) = (0.5, 0.5)
       ↓
2. Execute QAOA circuit with (γᵢ, βᵢ) on quantum backend (100 shots)
       ↓
3. Measure and decode bitstrings → TSP tours
       ↓
4. Calculate tour cost (objective function)
       ↓
5. Optimizer proposes new (γᵢ₊₁, βᵢ₊₁) based on gradient-free search
       ↓
6. Repeat until convergence (~10-50 iterations)
       ↓
7. Final execution with optimized (γ*, β*) using 1000 shots
```

**Performance Considerations:**
- **Optimization cost:** ~20-50 iterations × 100 shots = 2,000-5,000 extra shots
- **Time:** +10-30 seconds on local simulator, +2-5 minutes on cloud backends
- **Quality:** 5-20% better tour quality (problem-dependent)

### Disable Optimization (Backward Compatibility)

Use fixed parameters without optimization:

```fsharp
// Use hardcoded parameters (γ=0.5, β=0.5)
let result = solveWithShots backend distances 1000
```

### Compare Classical vs. Quantum

```fsharp
open System.Diagnostics

// Classical solution
let sw = Stopwatch.StartNew()
let classicalSolution = TspSolver.solveWithDistances distances TspSolver.defaultConfig
sw.Stop()
printfn "Classical: %.2f (%.2f ms)" classicalSolution.TourLength sw.Elapsed.TotalMilliseconds

// Quantum solution with optimization
let sw2 = Stopwatch.StartNew()
match solve backend distances defaultConfig with
| Ok quantumSolution ->
    sw2.Stop()
    printfn "Quantum: %.2f (%.2f ms)" quantumSolution.TourLength sw2.Elapsed.TotalMilliseconds
    printfn "Optimization iterations: %d" quantumSolution.OptimizationIterations
| Error msg ->
    printfn "Quantum error: %s" msg
```

## Tips for Best Results

**Classical Solver:**
1. **Problem Size**: Works well up to ~100 cities
2. **Iterations**: Increase for better solutions (at cost of time)
3. **Cooling Rate**: 0.995-0.999 gives good balance
4. **Initial Temperature**: Higher = more exploration early on
5. **Random Seed**: Use `Some seed` for reproducible results

**Quantum Solver (QAOA with Optimization):**
1. **Problem Size**: 3-8 cities (limited by qubits: N cities → N² qubits)
2. **Optimization Shots**: 100 for local, 50 for cloud (cost savings)
3. **Final Shots**: 1000+ for accurate measurement statistics
4. **Initial Parameters**: (0.5, 0.5) is a good default for most problems
5. **Backend Choice**: Local for testing (<16 qubits), IonQ/Rigetti for larger problems

## See Also

- **[Quantum TSP Example](quantum-tsp-example.md)** - Deep dive into QAOA with optimization
- [Portfolio Example](portfolio-example.md) - Portfolio optimization
- [Hybrid Solver Guide](hybrid-solver.md) - Advanced hybrid solver usage
- [Quantum vs Classical](../guides/quantum-vs-classical.md) - Decision framework
- [API Reference](../api-reference.md) - Complete API documentation
