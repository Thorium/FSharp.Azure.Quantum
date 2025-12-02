# Knapsack Example - Resource Allocation with Quantum QAOA

## Overview

This example demonstrates the **0/1 Knapsack Problem** - a fundamental combinatorial optimization problem. Given items with weights and values, and a capacity constraint, select a subset that maximizes total value without exceeding capacity.

## Use Cases

### 1. Software Project Selection (Budget Allocation)
**Problem**: Select projects to maximize business value within budget constraint.
- **Input**: Projects with costs and expected benefits, quarterly budget
- **Output**: Optimal project portfolio maximizing ROI
- **Business Value**: Strategic investment planning, maximized returns

### 2. Cargo Loading Optimization
**Problem**: Load truck/ship to maximize cargo value within weight limit.
- **Input**: Items with weights and values, vehicle capacity
- **Output**: Optimal loading plan maximizing transported value
- **Business Value**: Logistics efficiency, revenue maximization

### 3. Sprint Task Selection
**Problem**: Choose tasks to maximize priority points within time constraint.
- **Input**: Tasks with durations and priorities, sprint capacity
- **Output**: Optimal sprint backlog
- **Business Value**: Agile planning, team productivity

### 4. Classic Knapsack
**Problem**: Textbook example with gold/silver/bronze bars.
- **Input**: Items with integer weights/values, capacity
- **Output**: Optimal selection
- **Business Value**: Educational, algorithm verification

## Examples Included

### Example 1: Software Project Selection
8 projects (API Rewrite, Mobile App, Dashboard, etc.) with $300k budget.
```fsharp
let projects = [
    ("API Rewrite", 150000.0, 500000.0)          // cost, benefit
    ("Mobile App", 100000.0, 350000.0)
    // ...
]

match Knapsack.solve problem None with
| Ok solution -> 
    printfn "Total Benefit: $%.0f" solution.TotalValue
    printfn "Capacity Utilization: %.1f%%" solution.CapacityUtilization
```

**Output**:
- Selected: API Rewrite, Dashboard UI, Performance Optimization, Security Audit
- Total Benefit: $1,070,000
- Capacity Utilization: 100%
- ROI: 3.57x

### Example 2: Cargo Loading
8 cargo types (Electronics, Furniture, Jewelry, etc.) with 600kg truck.
```fsharp
let cargo = [
    ("Electronics", 100.0, 50000.0)  // weight_kg, value_usd
    ("Jewelry", 10.0, 80000.0)       // Excellent value density!
    // ...
]

let problem = Knapsack.cargoLoading cargo 600.0
```

**Output**:
- Selected: Jewelry, Computers, Electronics, Tools, Textiles
- Total Value: $228,000
- Weight: 510kg / 600kg (85% utilization)

### Example 3: Sprint Task Selection
8 tasks with 40-hour sprint capacity.
```fsharp
let tasks = [
    ("Critical Bug Fix", 4.0, 100.0)  // hours, priority
    ("Feature Request A", 8.0, 60.0)
    // ...
]
```

**Output**:
- Selected: 7 tasks (32 hours)
- Total Priority: 470 points
- Remaining Time: 8 hours

### Example 4: Classic Knapsack
Textbook example with 3 metal bars.
```fsharp
let items = [
    ("Gold Bar", 10.0, 60.0)
    ("Silver Bar", 20.0, 100.0)
    ("Bronze Bar", 30.0, 120.0)
]
let capacity = 50.0
```

**Output**:
- Selected: Silver Bar + Bronze Bar
- Value: 220 (optimal!)

### Example 5: Solution Validation
Demonstrates validation utilities:
```fsharp
let isFeasible = Knapsack.isFeasible problem selectedItems
let totalValue = Knapsack.totalValue selectedItems
let efficiency = Knapsack.efficiency selectedItems  // value/weight ratio
```

### Example 6: Algorithm Comparison
Compares 3 algorithms on random instance:
- **Quantum QAOA**: Heuristic quantum solution
- **Classical Greedy**: Fast heuristic (value/weight ratio)
- **Classical DP**: Exact optimal solution (O(n*W) time)

## Quantum vs Classical

Each example demonstrates:
1. **Quantum QAOA** - Uses local quantum simulation (or cloud backends)
2. **Classical Greedy** - Fast heuristic baseline
3. **Classical DP** - Exact optimal solution for comparison

```fsharp
// Quantum solution
let qSol = Knapsack.solve problem None

// Classical greedy
let greedySol = Knapsack.solveClassicalGreedy problem

// Classical DP (optimal)
let dpSol = Knapsack.solveClassicalDP problem
```

## Running the Example

```bash
dotnet fsi Knapsack.fsx
```

## API Highlights

### Simple API (Quantum-First)
```fsharp
// Automatic quantum simulation
let solution = Knapsack.solve problem None

// Cloud quantum backend
let ionqBackend = BackendAbstraction.createIonQBackend(...)
let solution = Knapsack.solve problem (Some ionqBackend)
```

### Helper Functions
```fsharp
// Mock parameters for demonstrations
let projects = [("Project1", 1000.0, 5000.0)]
let budget = 10000.0
let cargo = [("Item1", 50.0, 100.0)]
let capacity_kg = 200.0
let tasks = [("Task1", 2.0, 10.0)]
let available_hours = 8.0
let numItems = 10
let maxWeight = 100.0
let maxValue = 1000.0
let capacityRatio = 0.5

let _ = Knapsack.budgetAllocation projects budget
let _ = Knapsack.cargoLoading cargo capacity_kg
let _ = Knapsack.taskScheduling tasks available_hours
let _ = Knapsack.randomInstance numItems maxWeight maxValue capacityRatio
```

### Classical Solvers
```fsharp
let _ = Knapsack.solveClassicalGreedy problem   // Fast heuristic
let _ = Knapsack.solveClassicalDP problem        // Exact optimal (O(n*W))
```

### Validation
```fsharp
let selectedItems = [items.[0]; items.[1]]

let _ = Knapsack.isFeasible problem selectedItems
let _ = Knapsack.totalValue selectedItems
let _ = Knapsack.totalWeight selectedItems
let _ = Knapsack.efficiency selectedItems  // value/weight ratio
```

## Algorithm Complexity

| Algorithm | Time Complexity | Space | Optimality |
|-----------|----------------|-------|------------|
| Quantum QAOA | O(shots × circuit_depth) | O(2^n) | Heuristic |
| Classical Greedy | O(n log n) | O(1) | ~80-90% optimal |
| Classical DP | O(n × W) | O(n × W) | Optimal |

Where:
- n = number of items
- W = capacity (scaled to integer)
- shots = QAOA measurement shots (default 1000)

## Problem Characteristics

| Example | Items | Capacity Type | Optimal Strategy |
|---------|-------|---------------|------------------|
| Project Selection | 8 | Budget ($) | Value density + budget fit |
| Cargo Loading | 8 | Weight (kg) | Value density ($/kg) |
| Sprint Tasks | 8 | Time (hours) | Priority + time fit |
| Classic | 3 | Weight | Optimal: Silver + Bronze |

## Key Insights

1. **Quantum QAOA**: Finds good solutions for small-medium instances (< 16 items due to qubit limit)
2. **Classical Greedy**: Fast but suboptimal (typically 80-90% of optimal)
3. **Classical DP**: Exact optimal but scales poorly with capacity (pseudo-polynomial)
4. **Value Density**: Items with high value/weight ratio typically selected first (greedy heuristic)

## Next Steps

1. **Experiment** with different problem instances
2. **Compare** quantum vs classical solution quality
3. **Scale up** to larger problems (LocalBackend supports up to 16 items = 16 qubits)
4. **Try cloud backends** for real quantum hardware execution:
   ```fsharp
   let ionqBackend = BackendAbstraction.createIonQBackend(workspace, target)
   let solution = Knapsack.solve problem (Some ionqBackend)
   ```

## References

- **Knapsack Problem**: [Wikipedia](https://en.wikipedia.org/wiki/Knapsack_problem)
- **Dynamic Programming Solution**: Classic O(n*W) algorithm
- **QAOA for Knapsack**: Quantum approximation with penalty for capacity constraint
- **Applications**: Resource allocation, logistics, scheduling, portfolio optimization
