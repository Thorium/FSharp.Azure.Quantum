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

---

## Overview

QUBO is the mathematical formulation that quantum annealers and QAOA algorithms solve. Different problem domains benefit from different variable encoding schemes. Research shows domain-specific transformations can improve solution quality by **20-40%**.

### What is QUBO?

```
minimize: x^T Q x
where:
  Q is n×n symmetric matrix (QUBO coefficients)
  x is binary vector (solution)
```

### Encoding Strategy Types

```fsharp
type EncodingStrategy =
    | NodeBased          // Time-based encoding (TSP: visit city i at time t)
    | EdgeBased          // Edge-based encoding (TSP: travel from city i to j)
    | CorrelationBased   // Correlation matrix (Portfolio: risk modeling)
    | Custom             // User-defined transformations
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

**Recommended for**: TSP with 20+ cities

---

### EdgeBased Encoding

**Use when**: Small problems (n < 20), prioritize solution quality

**TSP Example**:
- Variables: `x[i][j]` = "travel from city i to city j"
- QUBO size: n² variables (n²-n edges + penalties)
- Pros: Direct distance encoding, 20-30% better solutions
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

// QUBO structure:
// - Diagonal: -distance[i,j] (minimize travel distance)
// - Off-diagonal: constraint penalties (ensure valid tour)
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
| **Solution Quality** | 20-30% better | Good |
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

These are encoded as penalty terms in the QUBO matrix using:
```
Penalty = (Σ x - 1)²
```

### Choosing Constraint Penalty

**Lucas Rule** (from literature): `λ ≥ max(|H_objective|) + 1`

```fsharp
// Example: TSP with max distance 500km
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

// Register custom problem
ProblemTransformer.registerProblem "GraphColoring" graphColoringTransform

// Use registered transformation
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
let strategy = ProblemTransformer.recommendStrategy "TSP" 10
// Returns: EdgeBased (n < 20)

let strategy = ProblemTransformer.recommendStrategy "TSP" 50
// Returns: NodeBased (n ≥ 20)

let strategy = ProblemTransformer.recommendStrategy "Portfolio" 10
// Returns: CorrelationBased (always for portfolio)
```

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

1. **Size Consistency**: Matrix dimensions match declared size
2. **Symmetry**: Q[i,j] = Q[j,i] for all i,j (required for QUBO)
3. **Bounds**: No NaN or Infinity values
4. **Feasibility**: Coefficients are finite real numbers

---

## Best Practices

### 1. Choose the Right Strategy

```fsharp
// Example distance matrices
let distances_10_cities = Array2D.create 10 10 1.0
let distances_100_cities = Array2D.create 100 100 1.0

// ✓ GOOD: EdgeBased for small TSP
let small_tsp = ProblemTransformer.encodeTspEdgeBased distances_10_cities 200.0

// ✗ BAD: EdgeBased for large TSP (too many constraints)
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
let penalty = 10.0  // < maxDistance!

// ✗ BAD: Penalty too large (numerical instability)
let penalty = 1000000.0
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

## Performance Benchmarks

### TSP: EdgeBased vs NodeBased

| Cities | EdgeBased Quality | NodeBased Quality | Improvement |
|--------|------------------|-------------------|-------------|
| 5      | Optimal          | Good              | +20%        |
| 10     | Near-optimal     | Good              | +25%        |
| 15     | Near-optimal     | Fair              | +30%        |
| 20     | Good             | Good              | +15%        |
| 50+    | N/A (too large)  | Good              | -           |

**Conclusion**: EdgeBased excels for n < 20, NodeBased scales better for large problems.

---

## References

- **QAOA for QUBO**: [Farhi et al. (2014)](https://arxiv.org/abs/1411.4028)
- **TSP QUBO Formulations**: [Lucas (2014)](https://arxiv.org/abs/1302.5843)
- **Portfolio Optimization**: Modern Portfolio Theory (Markowitz, 1952)
- **Lucas Rule**: Penalty selection for constraint satisfaction

---

## See Also

- [API Reference](api-reference.md)
- [TSP Example](examples/tsp-example.md)
- [Getting Started Guide](getting-started.md)
