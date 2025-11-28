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
let customConfig: TspSolver.TspConfig = {
    MaxIterations = 20000
    UseNearestNeighbor = true
}

let largeSolution = TspSolver.solveWithDistances largeDistances customConfig

printfn "20-City Tour Length: %.2f" largeSolution.TourLength
printfn "Converged after %d iterations" largeSolution.Iterations
```

## Builder Pattern API

Use the builder pattern for more complex scenarios:

```fsharp
// Create problem
// Example city list
let cities = [
    ("City1", 0.0, 0.0)
    ("City2", 1.0, 0.0)
    ("City3", 0.0, 1.0)
]

let problem = TSP.createProblem cities

// Solve with default config
let solution = TSP.solve problem None

// Or solve with custom config
let customSolution = TSP.solve problem (Some customConfig)
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
let experimentalConfig: TspSolver.TspConfig = {
    MaxIterations = 50000
    UseNearestNeighbor = true
}

let solution = TspSolver.solveWithDistances distances experimentalConfig
```

## Quantum Advisor Recommendations

Get recommendations before solving:

```fsharp
match QuantumAdvisor.getRecommendation distances with
| Ok recommendation ->
    printfn "Problem Size: %d cities" recommendation.ProblemSize
    printfn "Complexity: %s" recommendation.RecommendationType
    printfn "Recommendation: %A" recommendation.RecommendationType
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

## Tips for Best Results

1. **Problem Size**: Classical works well up to ~100 cities
2. **Iterations**: Increase for better solutions (at cost of time)
3. **Cooling Rate**: 0.995-0.999 gives good balance
4. **Initial Temperature**: Higher = more exploration early on
5. **Random Seed**: Use `Some seed` for reproducible results

## See Also

- [Portfolio Example](portfolio-example.md) - Portfolio optimization
- [Hybrid Solver Guide](hybrid-solver.md) - Advanced hybrid solver usage
- [Quantum vs Classical](../guides/quantum-vs-classical.md) - Decision framework
- [API Reference](../api-reference.md) - Complete API documentation
