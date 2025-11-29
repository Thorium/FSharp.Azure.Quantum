# MaxCut Example - Graph Partitioning with Quantum QAOA

## Overview

This example demonstrates **MaxCut** - THE canonical QAOA (Quantum Approximate Optimization Algorithm) problem. MaxCut partitions a graph into two sets to maximize the total weight of edges crossing the partition.

## Use Cases

### 1. Circuit Design Wire Minimization
**Problem**: Partition circuit blocks into two regions (left/right chip halves) to minimize wire crossings.
- **Input**: Circuit blocks and their interconnection weights
- **Output**: Optimal partition that minimizes communication overhead
- **Business Value**: Reduced manufacturing cost, improved signal integrity

### 2. Social Network Community Detection
**Problem**: Identify polarized communities or natural groupings in social networks.
- **Input**: People and their connection strengths
- **Output**: Two distinct communities with maximal separation
- **Business Value**: Content recommendation, targeted advertising, understanding group dynamics

### 3. Load Balancing
**Problem**: Distribute tasks across two servers to minimize inter-server communication.
- **Input**: Tasks and their communication patterns
- **Output**: Optimal task assignment minimizing network traffic
- **Business Value**: Reduced latency, improved system performance

### 4. Image Segmentation
**Problem**: Separate image into foreground and background.
- **Input**: Pixels and their similarity weights
- **Output**: Binary segmentation maximizing contrast
- **Business Value**: Object detection, medical imaging, computer vision

## Examples Included

### Example 1: Circuit Design
4-block circuit (CPU, GPU, RAM, IO) with weighted interconnections.
```fsharp
let blocks = ["CPU"; "GPU"; "RAM"; "IO"]
let interconnects = [
    ("CPU", "RAM", 10.0)  // High bandwidth
    ("CPU", "GPU", 5.0)   // Compute communication
    // ...
]

match MaxCut.solve problem None with
| Ok solution -> 
    printfn "Partition A: %A" solution.PartitionS
    printfn "Partition B: %A" solution.PartitionT
    printfn "Cut Value: %.1f" solution.CutValue
```

### Example 2: Helper Functions
Demonstrates built-in graph generators:
- **Complete Graph (K_n)**: All vertices connected
- **Cycle Graph (C_n)**: Vertices in a ring
- **Star Graph**: One central hub with spokes
- **Grid Graph**: 2D lattice structure
- **Path Graph**: Linear chain of vertices

```fsharp
let graph = MaxCut.completeGraph ["A"; "B"; "C"; "D"] 1.0
let solution = MaxCut.solve graph None
```

### Example 3: Social Network
6-person network with two natural communities.
```fsharp
let people = ["Alice"; "Bob"; "Charlie"; "David"; "Eve"; "Frank"]
let connections = [
    ("Alice", "Bob", 5.0)     // Close friends
    ("David", "Eve", 6.0)     // Different group
    ("Charlie", "David", 1.0) // Weak bridge
]
```

### Example 4: One-Step API
Solve without explicit problem creation:
```fsharp
let vertices = ["X"; "Y"; "Z"]
let edges = [("X", "Y", 2.0); ("Y", "Z", 3.0); ("Z", "X", 1.0)]
let solution = MaxCut.solveDirectly vertices edges None
```

### Example 5: Validation
Verify custom partitions and calculate cut values:
```fsharp
let isValid = MaxCut.isValidPartition problem partitionS partitionT
let cutValue = MaxCut.calculateCutValue problem partitionS
```

## Quantum vs Classical

Each example compares:
- **Quantum QAOA**: Uses local quantum simulation (or cloud backends)
- **Classical Greedy**: Local search baseline for comparison

```fsharp
// Quantum solution
let quantumSolution = MaxCut.solve problem None

// Classical baseline
let classicalSolution = MaxCut.solveClassical problem
```

## Running the Example

```bash
dotnet fsi MaxCut.fsx
```

## API Highlights

### Simple API (Quantum-First)
```fsharp
// Automatic quantum simulation
let solution = MaxCut.solve problem None

// Cloud quantum backend
let ionqBackend = BackendAbstraction.createIonQBackend(...)
let solution = MaxCut.solve problem (Some ionqBackend)
```

### Helper Functions
```fsharp
MaxCut.completeGraph vertices weight
MaxCut.cycleGraph vertices weight
MaxCut.starGraph center spokes weight
MaxCut.gridGraph rows cols weight
MaxCut.pathGraph vertices weight
```

### Validation
```fsharp
MaxCut.isValidPartition problem partitionS partitionT
MaxCut.calculateCutValue problem partitionS
```

## Problem Characteristics

| Graph Type | Vertices | Edges | Optimal Cut (unit weights) |
|------------|----------|-------|---------------------------|
| K_4 (Complete) | 4 | 6 | 4 |
| C_4 (Cycle) | 4 | 4 | 4 |
| Star (3 spokes) | 4 | 3 | 3 |
| Grid (2×3) | 6 | 7 | 7 |

## Next Steps

1. **Experiment** with different graph structures
2. **Compare** quantum vs classical solution quality
3. **Scale up** to larger graphs (LocalBackend supports up to 16 vertices)
4. **Try cloud backends** for real quantum hardware execution:
   ```fsharp
   let ionqBackend = BackendAbstraction.createIonQBackend(workspace, target)
   let solution = MaxCut.solve problem (Some ionqBackend)
   ```

## References

- **MaxCut Problem**: [Wikipedia](https://en.wikipedia.org/wiki/Maximum_cut)
- **QAOA Algorithm**: [Original Paper (Farhi et al. 2014)](https://arxiv.org/abs/1411.4028)
- **Applications**: VLSI Design, Network Optimization, Machine Learning
