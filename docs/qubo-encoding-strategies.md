---
layout: default
title: QUBO Encoding Strategies
---

# QUBO Encoding Strategies

Guide to problem-specific QUBO (Quadratic Unconstrained Binary Optimization) transformation strategies for optimal quantum circuit performance.

## Table of Contents

- [Overview](#overview)
- [Encoding Strategies](#encoding-strategies)
- [TSP Encoding](#tsp-encoding)
- [Portfolio Optimization](#portfolio-optimization)
- [Custom Problem Registration](#custom-problem-registration)
- [Strategy Selection](#strategy-selection)
- [Validation](#validation)
- [Best Practices](#best-practices)
- [Sparse QUBO Pipeline](#sparse-qubo-pipeline)

---

## Overview

QUBO is the mathematical formulation that quantum annealers and QAOA algorithms solve. Different problem domains benefit from different variable encoding schemes, and the choice affects both the number of variables and how directly the objective appears in the matrix.

### What is QUBO?

```
minimize: x^T Q x
where:
  Q is n×n symmetric matrix (QUBO coefficients)
  x is binary vector (solution)
```

### Encoding Strategy Types

`EncodingStrategy` (namespace `FSharp.Azure.Quantum`) has four cases:

```fsharp
open FSharp.Azure.Quantum

let strategies : EncodingStrategy list = [
    NodeBased                  // Time-based encoding (TSP: visit city i at time t)
    EdgeBased                  // Edge-based encoding (TSP: travel from city i to j)
    CorrelationBased           // Correlation matrix (Portfolio: risk modeling)
    Custom(fun n -> n * n)     // User-defined: maps problem size to QUBO size
]
```

---

## Encoding Strategies

### NodeBased Encoding

**Use when**: Large problems (n ≥ 20), need scalability

**TSP Example**:
- Variables: `x[i][t]` = "visit city i at time t"
- QUBO size: n² variables
- Pros: Scalable, well-understood
- Cons: Indirect distance encoding

`ProblemTransformer` has no node-based encoder of its own; the node-based TSP QUBO is built by `GraphOptimization.toQubo` for a problem with the `MinimizeTotalWeight` objective.

**Recommended for**: TSP with 20+ cities

---

### EdgeBased Encoding

**Use when**: Small problems (n < 20), prioritize solution quality

**TSP Example**:
- Variables: `x[i][j]` = "travel from city i to city j"
- QUBO size: n² variables (n²-n edges + penalties)
- Pros: Direct distance encoding (one coefficient per edge)
- Cons: More complex constraints

```fsharp
open FSharp.Azure.Quantum

// 5-city TSP with edge-based encoding
let distances = 
    array2D [[0.0; 10.0; 15.0; 20.0; 25.0]
             [10.0; 0.0; 35.0; 25.0; 30.0]
             [15.0; 35.0; 0.0; 30.0; 20.0]
             [20.0; 25.0; 30.0; 0.0; 15.0]
             [25.0; 30.0; 20.0; 15.0; 0.0]]

let constraintPenalty = 200.0  // Must exceed max distance

let qubo = ProblemTransformer.encodeTspEdgeBased distances constraintPenalty
// qubo.Size = 25, qubo.VariableNames = ["edge_0_0"; "edge_0_1"; ...]

// QUBO structure:
// - Diagonal: -distance[i,j] for edge i->j, constraintPenalty on self-loops i->i
// - Off-diagonal: +constraintPenalty for each pair of edges entering the same
//   city or leaving the same city
```

**Recommended for**: TSP with 5-15 cities, when solution quality matters

---

### CorrelationBased Encoding

**Use when**: Portfolio optimization with risk modeling

**Formula**:
```
Q = -returns + λ * Σ
where:
  returns = expected return vector
  Σ = covariance matrix (risk)
  λ = risk weight (risk aversion parameter)
```

```fsharp
open FSharp.Azure.Quantum

// 3-asset portfolio with risk modeling
let returns = [|0.12; 0.15; 0.08|]  // Expected returns (12%, 15%, 8%)

let covariance = 
    array2D [[0.04; 0.01; 0.02]  // Covariance matrix (risk)
             [0.01; 0.09; 0.03]
             [0.02; 0.03; 0.05]]

let riskWeight = 0.5  // Risk aversion: 0.0 (aggressive) to 1.0 (conservative)

let qubo = ProblemTransformer.encodePortfolioCorrelation returns covariance riskWeight

// QUBO structure:
// - Diagonal Q[i,i] = -return[i] + λ * variance[i]
// - Off-diagonal Q[i,j] = λ * covariance[i,j]
// Balances return maximization vs risk minimization
```

**Recommended for**: All portfolio optimization problems

---

## TSP Encoding

### Comparison: EdgeBased vs NodeBased

| Criterion | EdgeBased | NodeBased |
|-----------|-----------|-----------|
| **Problem Size** | n < 20 cities | n ≥ 20 cities |
| **QUBO Size** | n² variables | n² variables |
| **Distance Encoding** | Direct (diagonal) | Indirect (constraints) |
| **Constraint Complexity** | Higher | Lower |
| **Recommended Use** | Quality-critical, small TSP | Large-scale TSP |

### EdgeBased TSP Constraints

Edge-based encoding requires two constraint types:

1. **Entry Constraint**: Each city entered exactly once
   ```
   For each city j:
     Σ(over all i) x[i][j] = 1
   ```

2. **Exit Constraint**: Each city exited exactly once
   ```
   For each city i:
     Σ(over all j) x[i][j] = 1
   ```

The textbook penalty for "exactly once" is:
```
Penalty = (Σ x - 1)²
```

`encodeTspEdgeBased` adds only the pairwise part of that penalty: `+constraintPenalty` for every pair of edges that enter the same city, or leave the same city. That discourages two entries or two exits, but the matrix has no linear term that rewards choosing exactly one edge.

### Choosing Constraint Penalty

**Lucas Rule** (from literature): `λ ≥ max(|H_objective|) + 1`

```fsharp
// Example: TSP with max distance 500km (distances as defined above)
let maxDistance = 500.0
let constraintPenalty = maxDistance + 1.0  // 501.0

// For better constraint enforcement, scale by problem size:
let n = 20  // number of cities
let scaledPenalty = (maxDistance + 1.0) * sqrt(float n)
// ≈ 501 * 4.47 ≈ 2240

let qubo = ProblemTransformer.encodeTspEdgeBased distances scaledPenalty
```

---

## Portfolio Optimization

### Modern Portfolio Theory (Markowitz)

Portfolio optimization balances two objectives:
1. **Maximize returns**: Higher expected gains
2. **Minimize risk**: Lower variance/volatility

```fsharp
// Risk aversion parameter (λ)
let conservative = 1.0  // Heavy risk penalty
let balanced = 0.5      // Equal weight return/risk
let aggressive = 0.1    // Prioritize returns

// Example: Conservative portfolio
let qubo = ProblemTransformer.encodePortfolioCorrelation 
               returns 
               covariance 
               conservative
```

### Covariance Matrix

The covariance matrix Σ captures **correlation between assets**:

```
Σ[i,j] = correlation between asset i and asset j

Diagonal Σ[i,i] = variance of asset i (risk)
Off-diagonal Σ[i,j] = covariance (correlation)
```

**Diversification**: Negative covariance → assets move oppositely → reduces risk

---

## Custom Problem Registration

Register custom QUBO transformations for new problem types.

### Example: Graph Coloring

```fsharp
open FSharp.Azure.Quantum

// Define custom transformation
let graphColoringTransform (problemData: obj) =
    let edges = problemData :?> (int * int) list
    let numVertices = 4
    let numColors = 3
    let size = numVertices * numColors
    
    let q = Array2D.zeroCreate<float> size size
    let penalty = 100.0
    
    // For each edge (u, v), penalize if both have same color
    for (u, v) in edges do
        for c in 0 .. numColors - 1 do
            let idxU = u * numColors + c
            let idxV = v * numColors + c
            q.[idxU, idxV] <- q.[idxU, idxV] + penalty
            q.[idxV, idxU] <- q.[idxV, idxU] + penalty
    
    // Variable names
    let varNames = 
        [for i in 0 .. numVertices - 1 do
            for c in 0 .. numColors - 1 do
                yield sprintf "v%d_c%d" i c]
    
    {
        Size = size
        Coefficients = q
        VariableNames = varNames
    }

// Register custom problem (the registry is process-wide; re-registering replaces it)
ProblemTransformer.registerProblem "GraphColoring" graphColoringTransform

// Use registered transformation (throws if the name was never registered)
let edges = [(0, 1); (1, 2); (2, 3); (0, 3)]
let qubo = ProblemTransformer.applyTransformation "GraphColoring" (box edges)

// Validate transformation
let validation = ProblemTransformer.validateTransformation qubo
if validation.IsValid then
    printfn "✓ Custom QUBO is valid"
else
    printfn "✗ Errors:"
    validation.Messages |> List.iter (printfn "  - %s")
```

---

## Strategy Selection

### Automatic Recommendation

```fsharp
open FSharp.Azure.Quantum

// Automatic strategy selection based on problem type and size
let smallTsp = ProblemTransformer.recommendStrategy "TSP" 10
// Returns: EdgeBased (n < 20)

let largeTsp = ProblemTransformer.recommendStrategy "TSP" 50
// Returns: NodeBased (n ≥ 20)

let portfolio = ProblemTransformer.recommendStrategy "Portfolio" 10
// Returns: CorrelationBased (always for portfolio)
```

The problem type is matched case-insensitively: `"tsp"` or `"traveling-salesman"`, and `"portfolio"` or `"portfolio-optimization"`. Any other type returns `NodeBased`.

### Manual Strategy Selection

```fsharp
// Example problem parameters
let problemSize = 15  // Number of cities
let prioritizeQuality = true  // Quality vs. size trade-off

// Calculate QUBO size for strategy
let quboSize = ProblemTransformer.calculateQuboSize EncodingStrategy.EdgeBased 15
printfn "EdgeBased TSP (15 cities): %d variables" quboSize  // 225

// Choose strategy based on constraints
let strategy =
    if problemSize < 20 && prioritizeQuality then
        EncodingStrategy.EdgeBased
    elif problemSize >= 100 then
        EncodingStrategy.NodeBased
    else
        EncodingStrategy.NodeBased  // Safe default
```

---

## Validation

Validate QUBO transformations before sending to quantum hardware.

### Validation Checks

```fsharp
open FSharp.Azure.Quantum

let qubo = ProblemTransformer.encodeTspEdgeBased distances 200.0

let validation = ProblemTransformer.validateTransformation qubo

match validation.IsValid with
| true -> 
    printfn "✓ QUBO is valid - ready for quantum execution"
| false ->
    printfn "✗ QUBO validation failed:"
    validation.Messages |> List.iter (printfn "  - %s")
```

### What Validation Checks

1. **Size Consistency**: Matrix dimensions match the declared `Size` (if not, the other checks are skipped)
2. **Symmetry**: Q[i,j] = Q[j,i] for all i,j, within 1e-10
3. **Finite values**: No NaN or Infinity coefficients

Validation does not check that the constraint penalties are large enough; that is up to the encoding.

---

## Best Practices

### 1. Choose the Right Strategy

```fsharp
// Example distance matrices
let distances_10_cities = Array2D.create 10 10 1.0
let distances_100_cities = Array2D.create 100 100 1.0

// ✓ GOOD: EdgeBased for small TSP
let small_tsp = ProblemTransformer.encodeTspEdgeBased distances_10_cities 200.0

// ✗ BAD: EdgeBased for large TSP (10,000 variables: an 800 MB dense matrix, and far too many qubits)
let large_tsp = ProblemTransformer.encodeTspEdgeBased distances_100_cities 200.0

// ✓ GOOD: Use automatic recommendation
let strategy = ProblemTransformer.recommendStrategy "TSP" 100
```

### 2. Set Appropriate Constraint Penalties

```fsharp
// ✓ GOOD: Use Lucas Rule with size scaling
let maxDistance = 500.0
let n = 20
let penalty = (maxDistance + 1.0) * sqrt(float n)

// ✗ BAD: Penalty too small (constraints violated)
let tooSmallPenalty = 10.0  // < maxDistance!

// ✗ BAD: Penalty too large (numerical instability)
let tooLargePenalty = 1000000.0
```

### 3. Validate Before Execution

```fsharp
// Mock function for demonstration
let createCustomQubo() = Array2D.create 10 10 1.0

// ✓ GOOD: Always validate
let qubo = ProblemTransformer.encodeTspEdgeBased distances penalty
let validation = ProblemTransformer.validateTransformation qubo
if not validation.IsValid then
    let errorMsg = validation.Messages |> String.concat "; "
    failwith (sprintf "Invalid QUBO: %s" errorMsg)

// ✗ BAD: Skip validation (waste quantum $$$)
let qubo2 = createCustomQubo()  // No validation!
// Send to expensive quantum hardware...
```

### 4. Balance Risk vs Return (Portfolio)

```fsharp
// ✓ GOOD: Adjust risk weight based on investment goals
let young_investor = 0.2    // Aggressive (prioritize returns)
let near_retirement = 0.8   // Conservative (minimize risk)

let qubo = ProblemTransformer.encodePortfolioCorrelation 
               returns covariance young_investor

// ✗ BAD: Ignore risk entirely
let qubo_no_risk = ProblemTransformer.encodePortfolioCorrelation 
                       returns covariance 0.0  // No risk modeling!
```

### 5. Test with Small Problems First

```fsharp
// ✓ GOOD: Test with toy problem
let test_distances = array2D [[0.0; 10.0]; [10.0; 0.0]]
let test_qubo = ProblemTransformer.encodeTspEdgeBased test_distances 20.0
let test_validation = ProblemTransformer.validateTransformation test_qubo
assert test_validation.IsValid

// Then scale to production
let production_distances = array2D [[0.0; 50.0; 100.0]; [50.0; 0.0; 75.0]; [100.0; 75.0; 0.0]]  // Mock 3-city distance matrix
let penalty = 100.0  // Penalty strength for constraint violations
let prod_qubo = ProblemTransformer.encodeTspEdgeBased production_distances penalty
```

---

## Sparse QUBO Pipeline

For large, sparse QUBO problems, the library provides a memory-efficient pipeline that operates on `Map<int * int, float>` instead of dense `float[,]` matrices. A 1000-variable problem with 5% density needs ~5,000 map entries versus a 1,000,000-element dense matrix.

### When to Use Sparse vs Dense

| Criterion | Dense (`float[,]`) | Sparse (`Map<int*int, float>`) |
|-----------|--------------------|---------------------------------|
| **Problem Size** | n < 500 variables | n ≥ 500 variables |
| **Density** | > 30% non-zero entries | < 30% non-zero entries |
| **Memory** | O(n²) always | O(k) where k = non-zero entries |
| **Use Case** | Small/medium fully-connected | Large graph-based, TSP, network |
| **Migration** | Existing solvers (`executeFromQubo`) | New solvers, custom problems |

**Rule of thumb**: If your QUBO matrix is mostly zeros, use the sparse pipeline.

The sparse pipeline saves the memory of the QUBO matrix only. Running QAOA still needs one qubit per variable, and simulating it on `LocalBackend` needs a 2ⁿ-amplitude state vector, so local runs are limited by memory (at most 30 qubits). Larger problems need a hardware backend.

The execution helpers live in `FSharp.Azure.Quantum.Core.QaoaExecutionHelpers`. The synchronous ones shown here are marked `[<Obsolete>]` in favour of their `...Async` variants (`executeFromQuboAsync`, `executeQaoaCircuitSparseAsync`, `executeQaoaWithGridSearchSparseAsync`), which take a `CancellationToken` and return a `Task`; they still work and keep the examples short.

### Building a ProblemHamiltonian from Sparse QUBO

`QaoaCircuit.ProblemHamiltonian.fromQuboSparse` converts a sparse QUBO map to a `ProblemHamiltonian` using the same Ising mapping as `fromQubo`, but without allocating a dense matrix.

**Signature**:
```fsharp
QaoaCircuit.ProblemHamiltonian.fromQuboSparse
    : numQubits:int -> quboMap:Map<int * int, float> -> ProblemHamiltonian
```

**Ising mapping** (`fromQubo` delegates to it, x_i = (1 − Z_i)/2, constant offset dropped):
- Diagonal Q_ii => `-Q_ii/2 * Z_i`
- Off-diagonal (i < j) => `(Q_ij + Q_ji)/4 * Z_i Z_j`, and `-(Q_ij + Q_ji)/4` added to both `Z_i` and `Z_j`

The map may contain entries in upper-triangle, lower-triangle, or both — symmetric entries are merged automatically. Keys are used as qubit indices without a range check, so keep them in `[0, numQubits)`.

```fsharp
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Backends.LocalBackend

let backend = LocalBackend() :> IQuantumBackend

// Sparse Max-Cut QUBO for a 4-cycle: each edge (i,j) adds -x_i - x_j + 2 x_i x_j
let quboMap =
    Map.ofList [
        (0, 0), -2.0; (1, 1), -2.0; (2, 2), -2.0; (3, 3), -2.0
        (0, 1), 2.0   // Edge 0-1
        (1, 2), 2.0   // Edge 1-2
        (2, 3), 2.0   // Edge 2-3
        (0, 3), 2.0   // Edge 0-3
    ]

// Convert to ProblemHamiltonian without allocating a 4×4 dense matrix
let problemHam = QaoaCircuit.ProblemHamiltonian.fromQuboSparse 4 quboMap
let mixerHam = QaoaCircuit.MixerHamiltonian.create 4

// Use with any existing QAOA execution function
let parameters = [| (0.5, 0.3) |]  // 1 layer: (gamma, beta)
let result = QaoaExecutionHelpers.executeQaoaCircuit backend problemHam mixerHam parameters 100
```

### Dense Migration Helper: `executeFromQubo`

`QaoaExecutionHelpers.executeFromQubo` is a convenience entry point for solvers that already have a dense `float[,]` QUBO matrix. It builds the circuit and returns measurements in one call; the quantum solvers (TSP, Knapsack, Portfolio, etc.) call it or its async twin `executeFromQuboAsync`.

**Signature**:
```fsharp
QaoaExecutionHelpers.executeFromQubo
    : backend:IQuantumBackend
    -> qubo:float[,]
    -> parameters:(float * float)[]
    -> shots:int
    -> Result<int[][], QuantumError>
```

```fsharp
open FSharp.Azure.Quantum.Core

// Dense 3×3 QUBO matrix (fully connected)
let qubo =
    array2D [[ -1.0;  0.5;  0.0 ]
             [  0.5; -2.0;  0.3 ]
             [  0.0;  0.3; -1.5 ]]

let parameters = [| (0.7, 0.4); (0.5, 0.3) |]  // 2 QAOA layers

// Single call: builds ProblemHamiltonian, MixerHamiltonian, executes circuit
match QaoaExecutionHelpers.executeFromQubo backend qubo parameters 200 with
| Ok measurements ->
    printfn "Got %d measurement shots" measurements.Length
    // measurements: int[][] — each row is a bitstring
| Error err ->
    printfn "Execution failed: %A" err
```

### Sparse Execution Functions

The sparse pipeline provides four functions that mirror their dense counterparts but accept `Map<int * int, float>` and require an explicit `numQubits` parameter.

#### `evaluateQuboSparse`

Evaluates the QUBO objective value for a given bitstring. Used internally by the optimization and grid-search functions to score candidate solutions.

**Signature**:
```fsharp
QaoaExecutionHelpers.evaluateQuboSparse
    : quboMap:Map<int * int, float> -> bits:int[] -> float
```

```fsharp
let quboMap = Map.ofList [ (0, 0), -3.0; (0, 1), 2.0; (1, 1), -1.0 ]

// Evaluate energy for bitstring [1; 0]
let energy = QaoaExecutionHelpers.evaluateQuboSparse quboMap [| 1; 0 |]
// energy = -3.0  (only diagonal Q_00 contributes)

// Evaluate energy for bitstring [1; 1]
let energy2 = QaoaExecutionHelpers.evaluateQuboSparse quboMap [| 1; 1 |]
// energy2 = -3.0 + 2.0 + (-1.0) = -2.0
```

#### `executeQaoaCircuitSparse`

Executes a single QAOA circuit from a sparse QUBO representation. Equivalent to `executeQaoaCircuit` but skips dense matrix allocation by calling `fromQuboSparse` internally.

**Signature**:
```fsharp
QaoaExecutionHelpers.executeQaoaCircuitSparse
    : backend:IQuantumBackend
    -> numQubits:int
    -> quboMap:Map<int * int, float>
    -> parameters:(float * float)[]
    -> shots:int
    -> Result<int[][], QuantumError>
```

```fsharp
open FSharp.Azure.Quantum.Core

// Sparse QUBO for a 5-qubit problem (only 6 non-zero entries)
let quboMap =
    Map.ofList [
        (0, 0), -2.0; (1, 1), -3.0; (2, 2), -1.0
        (0, 1),  1.5; (1, 3),  0.8; (3, 4), -1.2
    ]

let parameters = [| (0.5, 0.3) |]

match QaoaExecutionHelpers.executeQaoaCircuitSparse backend 5 quboMap parameters 100 with
| Ok measurements ->
    // Find best solution
    let best =
        measurements
        |> Array.minBy (QaoaExecutionHelpers.evaluateQuboSparse quboMap)
    printfn "Best bitstring: %A with energy %.4f" best
        (QaoaExecutionHelpers.evaluateQuboSparse quboMap best)
| Error err ->
    printfn "Error: %A" err
```

#### `executeQaoaWithOptimizationSparse`

Runs QAOA with bounded Nelder-Mead parameter optimization over a sparse QUBO (at most `MaxOptimizationIterations` iterations, `OptimizationShots` per evaluation), then samples `FinalShots` with the optimized parameters. Returns the lowest-energy bitstring seen, the optimized parameters, and a convergence flag.

`QaoaSolverConfig` is shared by all QAOA solvers. These two sparse helpers use `NumLayers`, `OptimizationShots`, `FinalShots` and `MaxOptimizationIterations` (all must be positive); `EnableOptimization` and `EnableConstraintRepair` are read by the higher-level solvers, not here, since the function you call already chooses the method. `QaoaExecutionHelpers.defaultConfig`, `fastConfig` and `highQualityConfig` are ready-made presets.

**Signature**:
```fsharp
QaoaExecutionHelpers.executeQaoaWithOptimizationSparse
    : backend:IQuantumBackend
    -> numQubits:int
    -> quboMap:Map<int * int, float>
    -> config:QaoaSolverConfig
    -> Result<int[] * (float * float)[] * bool, QuantumError>
```

```fsharp
open FSharp.Azure.Quantum.Core

let quboMap =
    Map.ofList [
        (0, 0), -2.0; (1, 1), -3.0
        (0, 1),  1.0
    ]

let config : QaoaExecutionHelpers.QaoaSolverConfig = {
    NumLayers = 2
    OptimizationShots = 50
    FinalShots = 200
    EnableOptimization = true
    EnableConstraintRepair = true
    MaxOptimizationIterations = 100
}

match QaoaExecutionHelpers.executeQaoaWithOptimizationSparse backend 2 quboMap config with
| Ok (bestBits, optimizedParams, converged) ->
    let energy = QaoaExecutionHelpers.evaluateQuboSparse quboMap bestBits
    printfn "Best: %A  Energy: %.4f  Converged: %b" bestBits energy converged
    printfn "Optimized params: %A" optimizedParams
| Error err ->
    printfn "Error: %A" err
```

#### `executeQaoaWithGridSearchSparse`

Runs QAOA with a fixed grid search over gamma/beta values (7 gammas × 5 betas, the same pair repeated for every layer) using a sparse QUBO, then samples `FinalShots` with the best pair. Useful when Nelder-Mead convergence is unreliable or for quick exploration.

**Signature**:
```fsharp
QaoaExecutionHelpers.executeQaoaWithGridSearchSparse
    : backend:IQuantumBackend
    -> numQubits:int
    -> quboMap:Map<int * int, float>
    -> config:QaoaSolverConfig
    -> Result<int[] * (float * float)[], QuantumError>
```

```fsharp
open FSharp.Azure.Quantum.Core

// Sparse QUBO over 12 variables with only a few couplings
// (12 qubits keeps a local state-vector simulation small)
let quboMap =
    // In practice, built from graph edges
    Map.ofList [
        (0, 5), -1.0; (3, 11), -1.0; (7, 9), -1.0
        (2, 2), -0.5; (8, 8), -0.5
    ]

let config = { QaoaExecutionHelpers.fastConfig with OptimizationShots = 30; FinalShots = 200 }

match QaoaExecutionHelpers.executeQaoaWithGridSearchSparse backend 12 quboMap config with
| Ok (bestBits, bestParams) ->
    let energy = QaoaExecutionHelpers.evaluateQuboSparse quboMap bestBits
    printfn "Grid search best energy: %.4f" energy
    printfn "Best parameters: %A" bestParams
| Error err ->
    printfn "Error: %A" err
```

### Sparse vs Dense: Complete Comparison

```fsharp
open FSharp.Azure.Quantum.Core

let config = QaoaExecutionHelpers.fastConfig

// === Dense path (existing solvers) ===
let denseQubo =
    array2D [[ -1.0;  0.5 ]
             [  0.5; -2.0 ]]

// One-shot execution
let denseResult = QaoaExecutionHelpers.executeFromQubo backend denseQubo [|(0.5, 0.3)|] 100

// === Sparse path (new, memory-efficient) ===
let sparseQubo = Map.ofList [ (0, 0), -1.0; (1, 1), -2.0; (0, 1), 0.5 ]

// One-shot execution (equivalent)
let sparseResult = QaoaExecutionHelpers.executeQaoaCircuitSparse backend 2 sparseQubo [|(0.5, 0.3)|] 100

// With optimization
let optimResult = QaoaExecutionHelpers.executeQaoaWithOptimizationSparse backend 2 sparseQubo config

// With grid search
let gridResult = QaoaExecutionHelpers.executeQaoaWithGridSearchSparse backend 2 sparseQubo config
```

---

## References

- **QAOA for QUBO**: [Farhi et al. (2014)](https://arxiv.org/abs/1411.4028)
- **TSP QUBO Formulations**: [Lucas (2014)](https://arxiv.org/abs/1302.5843)
- **Portfolio Optimization**: Modern Portfolio Theory (Markowitz, 1952)
- **Lucas Rule**: Penalty selection for constraint satisfaction

---

## See Also

- [API Reference](api-reference.md)
- [Getting Started Guide](getting-started.md)
